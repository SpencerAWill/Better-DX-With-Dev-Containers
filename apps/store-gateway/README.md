# Store Gateway

.NET worker service that runs on-prem at each store location. Acts as the bridge between the edge (store) and cloud environments.

## Purpose

Enables each store to operate independently during network outages by managing bidirectional data synchronization, event forwarding, and connectivity monitoring.

## Responsibilities

- **Data synchronization** — Pulls menu updates, pricing, and configuration from cloud to local store database. Pushes locally created orders, payment records, and KDS metrics to cloud when online.
- **Outbox forwarding** — Reads events from the local outbox table and publishes them to cloud Service Bus/Event Hubs when connectivity is available. Ensures at-least-once delivery with deduplication.
- **Connectivity monitoring** — Tracks cloud connection health. Signals edge services to switch between online and offline modes.
- **Conflict resolution** — Handles reconciliation when local and cloud state diverge after an outage (e.g., menu changes that occurred while offline).

## Data

Owns `store_db` via the `store-data` library. Stores outbox events, sync checkpoints, and connectivity logs. This database exists only at the edge — it has no cloud counterpart.

## Deployment

Runs as a long-lived background process (Linux systemd service, Windows service, or container) at each store location. Not serverless — must be always running to maintain sync state.
