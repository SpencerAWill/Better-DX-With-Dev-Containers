#!/bin/bash
set -euo pipefail  # Exit on error, undefined vars, and pipeline failures
IFS=$'\n\t'       # Stricter word splitting

# 1. Extract Docker DNS info BEFORE any flushing
DOCKER_DNS_RULES=$(iptables-save -t nat | grep "127\.0\.0\.11" || true)

# Flush existing rules and delete existing ipsets
iptables -F
iptables -X
iptables -t nat -F
iptables -t nat -X
iptables -t mangle -F
iptables -t mangle -X
ipset destroy allowed-domains 2>/dev/null || true

# 2. Selectively restore ONLY internal Docker DNS resolution
if [ -n "$DOCKER_DNS_RULES" ]; then
    echo "Restoring Docker DNS rules..."
    iptables -t nat -N DOCKER_OUTPUT 2>/dev/null || true
    iptables -t nat -N DOCKER_POSTROUTING 2>/dev/null || true
    echo "$DOCKER_DNS_RULES" | xargs -L 1 iptables -t nat
else
    echo "No Docker DNS rules to restore"
fi

# First allow DNS and localhost before any restrictions
# Allow outbound DNS
iptables -A OUTPUT -p udp --dport 53 -j ACCEPT
# Allow inbound DNS responses
iptables -A INPUT -p udp --sport 53 -j ACCEPT
# Allow outbound SSH
iptables -A OUTPUT -p tcp --dport 22 -j ACCEPT
# Allow inbound SSH responses
iptables -A INPUT -p tcp --sport 22 -m state --state ESTABLISHED -j ACCEPT
# Allow localhost
iptables -A INPUT -i lo -j ACCEPT
iptables -A OUTPUT -o lo -j ACCEPT

# Create ipset with CIDR support
ipset create allowed-domains hash:net

# Fetch GitHub meta information - cache for 1 hour to avoid a network round-trip on every start
GH_CACHE="/var/tmp/gh-meta-cache.json"
if [ -f "$GH_CACHE" ] && [ $(( $(date +%s) - $(stat -c %Y "$GH_CACHE") )) -lt 3600 ]; then
    echo "Using cached GitHub IP ranges ($(( ( $(date +%s) - $(stat -c %Y "$GH_CACHE") ) / 60 )) minutes old)..."
    gh_ranges=$(cat "$GH_CACHE")
else
    echo "Fetching GitHub IP ranges..."
    gh_ranges=$(curl -s https://api.github.com/meta)
    if [ -z "$gh_ranges" ]; then
        echo "ERROR: Failed to fetch GitHub IP ranges"
        exit 1
    fi
    echo "$gh_ranges" > "$GH_CACHE"
fi

if ! echo "$gh_ranges" | jq -e '.web and .api and .git' >/dev/null; then
    echo "ERROR: GitHub API response missing required fields"
    exit 1
fi

echo "Processing GitHub IPs..."
while read -r cidr; do
    if [[ ! "$cidr" =~ ^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}/[0-9]{1,2}$ ]]; then
        echo "ERROR: Invalid CIDR range from GitHub meta: $cidr"
        exit 1
    fi
    echo "Adding GitHub range $cidr"
    ipset add allowed-domains "$cidr" -exist
done < <(echo "$gh_ranges" | jq -r '(.web + .api + .git)[]' | aggregate -q)

# Resolve and add other allowed domains
DOMAINS_FILE="$(dirname "$0")/allowed-domains.txt"
if [ ! -f "$DOMAINS_FILE" ]; then
    echo "ERROR: allowed-domains.txt not found at $DOMAINS_FILE"
    exit 1
fi

# Configure dnsmasq for wildcard domain support (entries starting with *.)
# dnsmasq intercepts DNS queries and dynamically adds resolved IPs to the ipset
ORIG_NS=$(grep '^nameserver' /etc/resolv.conf | head -1 | awk '{print $2}')
if [ -z "$ORIG_NS" ]; then
    echo "ERROR: Could not determine original nameserver from /etc/resolv.conf"
    exit 1
fi

DNSMASQ_CONF="/tmp/dnsmasq-firewall.conf"
{
    echo "no-resolv"
    echo "server=$ORIG_NS"
    echo "listen-address=127.0.0.53"
    echo "bind-interfaces"
} > "$DNSMASQ_CONF"

HAS_WILDCARDS=false
while IFS= read -r line; do
    [[ -z "$line" || "$line" =~ ^# ]] && continue
    if [[ "$line" == \*.* ]]; then
        domain="${line#\*.}"
        echo "ipset=/$domain/allowed-domains" >> "$DNSMASQ_CONF"
        HAS_WILDCARDS=true
    fi
done < "$DOMAINS_FILE"

if [ "$HAS_WILDCARDS" = true ]; then
    echo "Starting dnsmasq for wildcard domain support (upstream: $ORIG_NS)..."
    dnsmasq --conf-file="$DNSMASQ_CONF"
    cp /etc/resolv.conf /tmp/resolv.conf.tmp
    sed "s/^nameserver .*/nameserver 127.0.0.53/" /tmp/resolv.conf.tmp > /etc/resolv.conf
    rm /tmp/resolv.conf.tmp
    echo "dnsmasq started, DNS routing through 127.0.0.53"
fi

# Resolve all non-wildcard domains in parallel, then add their IPs to the ipset
DNS_TMPDIR=$(mktemp -d)
trap 'rm -rf "$DNS_TMPDIR"' EXIT

echo "Resolving domains in parallel..."
while IFS= read -r domain; do
    [[ -z "$domain" || "$domain" =~ ^# || "$domain" == \*.* ]] && continue
    (
        ips=""
        for attempt in 1 2 3; do
            ips=$(dig +noall +answer A "$domain" | awk '$4 == "A" {print $5}')
            [ -n "$ips" ] && break
            [ "$attempt" -lt 3 ] && sleep 2
        done
        if [ -z "$ips" ]; then
            echo "ERROR" > "$DNS_TMPDIR/$domain"
        else
            echo "$ips" > "$DNS_TMPDIR/$domain"
        fi
    ) &
done < "$DOMAINS_FILE"
wait  # Wait for all parallel resolutions to finish

# Process results and add IPs to ipset
while IFS= read -r domain; do
    [[ -z "$domain" || "$domain" =~ ^# || "$domain" == \*.* ]] && continue
    result_file="$DNS_TMPDIR/$domain"
    if [ ! -f "$result_file" ] || [ "$(cat "$result_file")" = "ERROR" ]; then
        echo "ERROR: Failed to resolve $domain after 3 attempts"
        exit 1
    fi
    while IFS= read -r ip; do
        if [[ ! "$ip" =~ ^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$ ]]; then
            echo "ERROR: Invalid IP from DNS for $domain: $ip"
            exit 1
        fi
        echo "Adding $ip for $domain"
        ipset add allowed-domains "$ip" -exist
    done < "$result_file"
done < "$DOMAINS_FILE"

# Get host network from routing table (use actual subnet, fall back to /24 from gateway)
DEFAULT_IFACE=$(ip -4 route | awk '/^default/{print $5; exit}')
HOST_NETWORK=$(ip -4 route | awk -v iface="$DEFAULT_IFACE" '$0 !~ /default/ && $3 == iface {print $1; exit}')
if [ -z "$HOST_NETWORK" ]; then
    HOST_IP=$(ip -4 route | awk '/^default/{print $3; exit}')
    if [ -z "$HOST_IP" ]; then
        echo "ERROR: Failed to detect host IP"
        exit 1
    fi
    HOST_NETWORK=$(echo "$HOST_IP" | sed "s/\.[0-9]*$/.0\/24/")
fi
echo "Host network detected as: $HOST_NETWORK"

# Set up remaining iptables rules
iptables -A INPUT -s "$HOST_NETWORK" -j ACCEPT
iptables -A OUTPUT -d "$HOST_NETWORK" -j ACCEPT

# Set default policies to DROP first
iptables -P INPUT DROP
iptables -P FORWARD DROP
iptables -P OUTPUT DROP

# First allow established connections for already approved traffic
iptables -A INPUT -m state --state ESTABLISHED,RELATED -j ACCEPT
iptables -A OUTPUT -m state --state ESTABLISHED,RELATED -j ACCEPT

# Then allow only specific outbound traffic to allowed domains
iptables -A OUTPUT -m set --match-set allowed-domains dst -j ACCEPT

# Explicitly REJECT all other outbound traffic for immediate feedback
iptables -A OUTPUT -j REJECT --reject-with icmp-admin-prohibited

echo "Firewall configuration complete"
echo "Verifying firewall rules..."
if curl --connect-timeout 5 https://example.com >/dev/null 2>&1; then
    echo "ERROR: Firewall verification failed - was able to reach https://example.com"
    exit 1
else
    echo "Firewall verification passed - unable to reach https://example.com as expected"
fi

# Verify GitHub API access
if ! curl --connect-timeout 5 https://api.github.com/zen >/dev/null 2>&1; then
    echo "ERROR: Firewall verification failed - unable to reach https://api.github.com"
    exit 1
else
    echo "Firewall verification passed - able to reach https://api.github.com as expected"
fi