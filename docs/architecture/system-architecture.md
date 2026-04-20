# System Architecture

## Overview

The online ordering platform is a distributed system built for restaurant chains that need to operate reliably across multiple store locations — including during network outages. The architecture follows a hybrid cloud/edge model: cloud services handle online ordering and administration, while edge services run on-prem at each store to power kitchen operations and maintain local ordering capability.

Key architectural principles:

- **Database-per-bounded-context** — Each domain owns its data. Services communicate via APIs or events, never by sharing a database.
- **Event-driven decoupling** — Background processing and cross-service communication use asynchronous events (Service Bus for commands, Event Hubs for broadcasts).
- **Offline-first edge** — Store-level services can operate independently during network partitions. Data synchronizes when connectivity returns.
- **Independent deployability** — Each service has its own build, deployment, and scaling profile.

---

## Service Architecture

### Frontends

| Service           | Stack                    | Deploy Zone | Purpose                                    |
| ----------------- | ------------------------ | ----------- | ------------------------------------------ |
| `ordering-web`    | React 19, TanStack, Vite | Cloud       | Customer-facing online ordering website    |
| `ordering-mobile` | TBD                      | Cloud       | Customer-facing native mobile ordering app |
| `admin-web`       | TBD (likely React)       | Cloud       | Administrator back-office management UI    |
| `kds-web`         | TBD (likely React)       | Edge        | Kitchen Display System for kitchen staff   |

- `ordering-web` and `ordering-mobile` are public-facing, served from the cloud. Customers need internet to reach them.
- `kds-web` runs locally at each store on dedicated kitchen displays. It must work without cloud connectivity.
- `admin-web` is internal-only, used by a small number of restaurant administrators.

### APIs

| Service        | Stack                  | Deploy Zone  | Purpose                                                                   |
| -------------- | ---------------------- | ------------ | ------------------------------------------------------------------------- |
| `ordering-api` | ASP.NET Core (.NET 10) | Cloud + Edge | Order intake, cart, checkout — shared by ordering-web and ordering-mobile |
| `admin-api`    | ASP.NET Core (.NET 10) | Cloud        | Administration, reporting, system configuration                           |
| `kds-api`      | ASP.NET Core (.NET 10) | Edge         | Kitchen display backend — real-time order queue management                |
| `payment-api`  | ASP.NET Core (.NET 10) | Cloud + Edge | Payment processing via Stripe, store-and-forward at edge                  |
| `menu-api`     | ASP.NET Core (.NET 10) | Cloud + Edge | Menu data service with caching (REST), read replica at edge               |

- APIs marked "Cloud + Edge" are deployed in both zones. The edge instance handles local operations; the cloud instance handles online traffic.
- `kds-api` needs persistent connections (WebSockets / Server-Sent Events) for real-time order updates to kitchen displays.
- `payment-api` is isolated from other services for PCI compliance. At the edge, it queues payments for cloud processing via the outbox pattern.

### Background Services

| Service                      | Stack                               | Deploy Zone  | Purpose                                                |
| ---------------------------- | ----------------------------------- | ------------ | ------------------------------------------------------ |
| `order-processing-functions` | Azure Functions (.NET 10, isolated) | Cloud + Edge | Order lifecycle state machine, event-driven            |
| `notification-functions`     | Azure Functions (.NET 10, isolated) | Cloud        | Event-triggered notifications (email, push, SMS)       |
| `store-gateway`              | .NET Worker Service                 | Edge         | Sync agent, outbox forwarding, connectivity monitoring |

- `order-processing-functions` is the core domain logic. It consumes events (order placed, payment confirmed, KDS status updates) and produces events that other services react to.
- `notification-functions` is cloud-only — notifications are non-critical during an outage and are queued for delivery when connectivity returns.
- `store-gateway` is the bridge between edge and cloud. It runs continuously at each store, managing bidirectional data sync.

### Identity and Authentication (Infrastructure)

Authentication and authorization are provided by **Keycloakify / Azure Entra ID** as infrastructure, not as an application-level service. In development, a Keycloak sidecar runs locally (admin console at `localhost:8180`, admin/admin).

#### User-Facing Authentication

All APIs validate JWT Bearer tokens issued by the identity provider. Frontends use authorization code flow with PKCE. Different user types have different roles:

- **customer** — ordering-web, ordering-mobile users
- **admin** — admin-web users
- **kitchen** — kds-web users

#### Service-to-Service Authentication

Inter-service calls (e.g., ordering-api → payment-api, ordering-api → menu-api) use the **OAuth 2.0 client credentials flow** via Keycloak. Each service has its own Keycloak client with a client ID and secret:

- The calling service requests a token from Keycloak using its client credentials
- The token includes scopes that define what the calling service is allowed to do (e.g., ordering-api can create payment intents but not issue refunds)
- The receiving service validates the token and checks scopes before processing the request
- Tokens are cached by the calling service to avoid per-request Keycloak round-trips

**Edge / offline considerations:** During a network outage, edge services cannot reach a cloud Keycloak instance to obtain new tokens. Two mitigations:

- **Long-lived edge tokens** — Edge service client tokens are configured with a longer TTL (e.g., 24 hours) to survive typical outage durations
- **Local Keycloak at edge (future)** — For full offline resilience, each store could run a local Keycloak instance that the store-gateway keeps synchronized with the cloud realm. Not required for the initial demo.

---

## Data Architecture

### Database-per-Bounded-Context

Each domain has its own PostgreSQL database on a shared instance. Services access only their own database — cross-domain communication happens through APIs or events.

```
PostgreSQL Instance
├── ordering_db     ← ordering-api, order-processing-functions
├── menu_db         ← menu-api (exclusive)
├── payment_db      ← payment-api (exclusive, PCI-isolated)
├── admin_db        ← admin-api (exclusive)
├── kds_db          ← kds-api (exclusive, event-populated)
└── store_db        ← store-gateway (exclusive, edge only)
```

| Database      | Library         | Owner Services                           | Key Entities                                        |
| ------------- | --------------- | ---------------------------------------- | --------------------------------------------------- |
| `ordering_db` | `ordering-data` | ordering-api, order-processing-functions | Orders, order items, order status, customer info    |
| `menu_db`     | `menu-data`     | menu-api                                 | Categories, items, modifiers, pricing, availability |
| `payment_db`  | `payment-data`  | payment-api                              | Payment intents, transactions, refunds              |
| `admin_db`    | `admin-data`    | admin-api                                | Audit logs, system configuration, business settings |
| `kds_db`      | `kds-data`      | kds-api                                  | Active order queue, station routing, prep status    |
| `store_db`    | `store-data`    | store-gateway                            | Outbox events, sync checkpoints, connectivity logs  |

Each EF Core library project contains its own DbContext, entity definitions, and migrations. Databases are created by the application on startup (via EF Core migrations), not by infrastructure scripts.

### Why This Separation

**Payment isolation (PCI compliance):** Payment data lives in `payment_db`, accessed only by `payment-api`. A security breach in any other service cannot expose payment records. The compliance surface area is limited to a single service.

**KDS event-driven read model:** `kds_db` is NOT a copy of the ordering database. It's a purpose-built read model populated by events:

```
ordering-api                         kds-api
    │                                   │
    ▼                                   ▼
ordering_db                          kds_db
    │                                   ▲
    ▼                                   │
order-processing-functions ─────────────┘
    ▲                            (events: order confirmed,
    │                             order updated)
    └───────────────────────────────────┐
                                        │
                                    kds-api
                                 (events: prep started,
                                  order ready, order bumped)
```

This means:

- KDS only holds active orders — a small, fast dataset optimized for kitchen operations
- KDS can operate offline with its last-received orders
- No customer PII or payment data leaks into the kitchen's data store
- KDS and ordering can evolve their schemas independently

**Independent schema evolution:** Migrating the menu schema doesn't require coordinating with ordering deployments. Each team (if there were teams) owns their own data model.

**Fault isolation:** A menu database outage doesn't take down order processing.

### Entity ID Strategy

All entity primary keys use **UUIDv7** (`Guid.CreateVersion7()` in .NET 9+). This is critical for the hybrid cloud/edge architecture:

- **Globally unique without coordination** — Cloud and edge instances generate IDs independently. No central sequence server, no risk of collisions when the store-gateway syncs edge-created orders to the cloud.
- **Time-ordered** — UUIDv7 embeds a millisecond timestamp, so IDs are naturally chronological. This prevents B-tree index fragmentation in PostgreSQL that random UUIDv4 would cause on high-volume tables (orders, payment intents, outbox events).
- **Sortable** — Entities sort chronologically by primary key without needing a separate `CreatedAt` index for ordering queries.
- **Native .NET support** — No third-party libraries required.

Customer-facing references (e.g., `PublicOrderId` like "ORD-A7F3B2") are separate from the internal UUIDv7 primary key. The public ID is shorter and human-readable; the internal ID is for inter-service communication and database operations.

---

## Deployment Topology

```
CLOUD                                          EDGE (per store)
──────────────────────────────                 ──────────────────────────────

 ┌─────────────────────────────┐                ┌─────────────────────────────┐
 │  Frontends                  │                │  Frontends                  │
 │  ┌─────────────┐            │                │  ┌─────────────┐            │
 │  │ ordering-web │            │                │  │ kds-web      │            │
 │  │ ordering-mobile           │                │  └─────────────┘            │
 │  │ admin-web    │            │                │                             │
 │  └─────────────┘            │                │  APIs                       │
 │                             │                │  ┌─────────────┐            │
 │  APIs                       │                │  │ ordering-api │ (local)    │
 │  ┌─────────────┐            │                │  │ kds-api      │            │
 │  │ ordering-api │ (online)   │                │  │ payment-api  │ (offline)  │
 │  │ admin-api    │            │    ◄── sync ──►│  │ menu-api     │ (replica)  │
 │  │ payment-api  │ (Stripe)   │                │  └─────────────┘            │
 │  │ menu-api     │ (source)   │                │                             │
 │  └─────────────┘            │                │  Background                 │
 │                             │                │  ┌─────────────────────────┐ │
 │  Background                 │                │  │ order-processing-funcs  │ │
 │  ┌─────────────────────────┐│                │  │ store-gateway           │ │
 │  │ order-processing-funcs  ││                │  └─────────────────────────┘ │
 │  │ notification-funcs      ││                │                             │
 │  └─────────────────────────┘│                │  Data                       │
 │                             │                │  ┌─────────────┐            │
 │  Data                       │                │  │ PostgreSQL   │ (local)    │
 │  ┌─────────────┐            │                │  │ Redis        │ (local)    │
 │  │ PostgreSQL   │ (managed)  │                │  └─────────────┘            │
 │  │ Redis        │            │                │                             │
 │  │ Service Bus  │            │                └─────────────────────────────┘
 │  │ Event Hubs   │            │
 │  │ Cosmos DB    │            │
 │  │ Blob Storage │            │
 │  └─────────────┘            │
 └─────────────────────────────┘
```

### What Runs Where

**Cloud only:** ordering-web, ordering-mobile, admin-web, admin-api, notification-functions. These serve online customers and administrators — they inherently require internet connectivity.

**Edge only:** kds-web, kds-api, store-gateway. These are store-specific and must work without cloud connectivity.

**Both zones:** ordering-api, payment-api, menu-api, order-processing-functions. Cloud instances handle online traffic; edge instances handle in-store operations. The store-gateway synchronizes state between them.

---

## Communication Patterns

### Synchronous (HTTP)

Frontend-to-API calls and some API-to-API calls use synchronous HTTP:

- `ordering-web` → `ordering-api` (cart, checkout, order status)
- `ordering-api` → `payment-api` (create payment intent during checkout)
- `ordering-api` → `menu-api` (fetch menu data for validation)
- `admin-web` → `admin-api` → `menu-api` (menu management)
- `kds-web` → `kds-api` (order queue, status updates)

### Asynchronous Commands (Service Bus)

Point-to-point delivery of commands that must be processed exactly once:

- `order-placed` queue — triggers order processing pipeline
- `menu-updated` queue — triggers cache invalidation and catalog rebuild

### Event Broadcasts (Event Hubs)

Publish-subscribe broadcasting of facts that multiple consumers may care about:

- `order-events` hub — order lifecycle events (placed, confirmed, preparing, ready, completed)
- Consumers: notification-functions, analytics processors, store-gateway

### Store-and-Forward (Outbox Pattern)

Edge services write events to a local outbox table instead of directly to cloud messaging infrastructure:

```
Edge service → local outbox table → store-gateway → cloud Service Bus / Event Hubs
```

This enables offline operation: events accumulate locally during an outage and are forwarded when connectivity returns. The store-gateway handles deduplication to ensure at-least-once delivery semantics.

---

## Hybrid Cloud/Edge Model

### The Store Gateway

The `store-gateway` is a .NET worker service that runs continuously at each store. It is the single point of synchronization between edge and cloud:

- **Outbox forwarding** — Reads events from local outbox tables, publishes to cloud messaging when online
- **Data pull** — Pulls menu updates, pricing changes, and configuration from cloud to local databases
- **Data push** — Pushes locally created orders, payment records, and KDS metrics to cloud
- **Connectivity monitoring** — Tracks cloud connection health, signals edge services to switch between online and offline modes
- **Conflict resolution** — Reconciles local and cloud state after an outage (e.g., menu changes that occurred while offline)

### Network Outage Behavior

When a store loses cloud connectivity:

| Capability                   | Behavior                                                                      |
| ---------------------------- | ----------------------------------------------------------------------------- |
| KDS display + updates        | Fully operational — runs entirely on edge                                     |
| In-store order intake        | Operational — edge ordering-api writes to local ordering_db                   |
| Order state transitions      | Operational — edge order-processing-functions runs locally                    |
| Menu browsing                | Operational — serves from local menu_db replica                               |
| Payment capture              | Queued — payment-api writes to local outbox, processes via Stripe when online |
| Online ordering (web/mobile) | Unavailable — customers can't reach cloud services                            |
| Admin operations             | Unavailable — admin-web and admin-api are cloud-only                          |
| Notifications                | Queued — events accumulate in outbox, sent after reconnection                 |
| Analytics                    | Delayed — events forwarded to cloud after reconnection                        |

### Reconnection

When connectivity returns, the store-gateway:

1. Forwards all queued outbox events to cloud messaging
2. Pulls any menu/config changes that occurred during the outage
3. Pushes locally created orders and payment records to the cloud
4. Reconciles any conflicts (cloud state wins for menu/config; local state wins for orders created during outage)

---

## Fault Tolerance

### Service-Level Failures

| Failure                     | Impact                                              | Mitigation                                                 |
| --------------------------- | --------------------------------------------------- | ---------------------------------------------------------- |
| ordering-api (cloud) down   | Online ordering unavailable                         | Edge ordering-api still handles in-store orders            |
| ordering-api (edge) down    | In-store ordering at that location unavailable      | Online ordering still works; other stores unaffected       |
| kds-api down                | Kitchen displays go blank at that location          | Other stores unaffected; orders queue for when it recovers |
| payment-api down            | Payments queued locally                             | Outbox pattern ensures no payment is lost                  |
| menu-api (cloud) down       | Menu updates paused; edge replicas serve stale data | Stores continue with last-known menu                       |
| notification-functions down | Notifications delayed                               | Events remain in Service Bus queue; processed on recovery  |
| store-gateway down          | Sync paused; edge continues operating independently | Events queue locally; sync resumes on restart              |

### Data-Level Failures

| Failure               | Impact                          | Mitigation                                           |
| --------------------- | ------------------------------- | ---------------------------------------------------- |
| Cloud PostgreSQL down | Cloud APIs degrade              | Edge continues with local databases                  |
| Edge PostgreSQL down  | That store's edge services fail | Other stores unaffected; online ordering still works |
| Redis down            | Cache misses; higher DB load    | Services fall through to database reads              |
| Service Bus down      | Async commands queue locally    | Outbox pattern at edge; cloud retries on recovery    |

### Network Partition (Cloud/Edge Split)

This is the primary failure mode the architecture is designed for. During a partition:

- Edge operates autonomously on local data
- Cloud continues serving online customers
- Events accumulate in local outbox tables
- Store-gateway monitors connectivity and triggers reconciliation on reconnection
- No data is lost — eventual consistency between zones

---

## Naming Conventions

Apps follow a `{domain}-{platform}` pattern:

- **Domain** identifies the business area: `ordering`, `admin`, `kds`, `payment`, `menu`, `notification`, `order-processing`, `store`
- **Platform** identifies the deployment target: `web`, `mobile`, `api`, `functions`, `gateway`

Data libraries follow `{domain}-data` and map to `{domain}_db` databases.

Frontend apps and their corresponding APIs share the same domain prefix (`ordering-web` + `ordering-api`, `admin-web` + `admin-api`), making the relationship between them clear.

---

## Architectural Decision Records

Decisions made during system design that aren't obvious from the structure alone.

### Menu API uses REST, not GraphQL

The menu-api exposes a REST API rather than GraphQL. Reasons:

- **Known consumers** — All clients are controlled (ordering-web, ordering-mobile, admin-web, kds-web). GraphQL's flexibility benefits unknown/third-party consumers, which don't exist here.
- **Predictable access patterns** — Get full menu, get item by ID, get items by category, update an item. These map cleanly to REST resources with no over-fetching/under-fetching issues.
- **Simpler caching** — REST responses are trivially cacheable by URL with HTTP cache headers or a Redis layer. GraphQL caching is more complex since every query shape can differ.
- **Lower demo complexity** — REST is instantly understandable to anyone looking at the project.

### API gateway is infrastructure, not an application

An API gateway (routing, rate limiting, auth enforcement) was considered as a project in `apps/` and rejected. It belongs in infrastructure configuration (Azure API Management, NGINX, Envoy) rather than as application code we maintain. In the dev container, services are called directly on their own ports. In production, an infrastructure-level gateway would sit in front of the APIs.

### Admin API is a long-running API, not serverless

`admin-api` was considered as an Azure Functions project (serverless) since it has low traffic and sporadic usage. It was kept as an ASP.NET Core API because:

- **Future real-time requirements** — Admin features may need WebSocket/SSE connections for live dashboards, order monitoring, or inventory alerts. Serverless doesn't support persistent connections.
- **Simpler migration path** — Starting as an API and moving to serverless later is straightforward. The reverse (serverless to API for real-time support) is a larger rewrite.

### Ordering domain is event-sourced; other domains remain CRUD

The ordering domain is modeled as an append-only log of domain events (`OrderPlaced`, `OrderPaymentConfirmed`, `OrderConfirmed`, `OrderPreparing`, `OrderReady`, `OrderPickedUp`, `OrderCancelled`). An order's current state is a projection — maintained in `ordering_db` as a read-model table for fast queries, and rebuildable from the event log at any time. Menu, payment, admin, KDS, and store-gateway domains stay CRUD.

**Event log shape** (lives in `ordering_db`, owned by `ordering-data`):

```
OrderEvents       { Id (UUIDv7, event id),
                    AggregateId (UUIDv7, order id),
                    SequenceNumber (int, per-aggregate),
                    EventType (string),
                    Payload (jsonb),
                    OccurredAt,
                    CausationId, CorrelationId }

OrderReadModel    { AggregateId (PK), PublicOrderId, Status, UserId, CustomerEmail,
                    CustomerName, TotalInCents, StripePaymentIntentId,
                    LastSequenceApplied, CreatedAt, UpdatedAt }
```

Append-event + update-read-model + insert-outbox-row happen in one PostgreSQL transaction.

**Why event-source the ordering domain specifically:**

- **Edge/cloud reconciliation becomes log replay, not conflict resolution.** Events are immutable facts; edge and cloud produce different events (for different orders, or different lifecycle steps on the same order) that never conflict at the row level. The store-gateway forwards events and the cloud projects them. The "cloud wins vs. edge wins" rule for order state disappears.
- **Projections are first-class.** `kds_db`, Cosmos `KitchenTickets`, Cosmos `CustomerOrders`, and Cosmos `Analytics` are all projections of the same event stream. Today's architecture already describes them as derived views populated by events — event sourcing makes this literal rather than aspirational.
- **The outbox collapses into the event log.** The store-gateway outbox forwarder reads unsent rows from `OrderEvents` by `SequenceNumber` and publishes them to cloud Event Hubs. No separate outbox table for order events.
- **Free audit trail.** Required anyway for admin-api compliance reporting and dispute resolution.
- **Retroactive projections.** New read models (e.g., a delivery-time analytics view) can be built by replaying events without backfill migrations.

**Why not event-source other domains:**

- **Menu** — CRUD matches admin authoring patterns; relational integrity rules (a modifier belongs to an item, an item belongs to a category) are easier to enforce in SQL; the Cosmos `MenuCatalog` is already the denormalized read side.
- **Payment** — Stripe is the source of truth; `payment_db` is a local mirror for PCI-isolated records. Layering event sourcing on top widens the compliance surface without benefit.
- **Admin** — Primarily configuration plus audit. The audit table is already append-only; there's no aggregate lifecycle to model.
- **KDS / store-gateway** — Read models and operational data, not aggregates.

**Event store choice: PostgreSQL, not a dedicated event store.** EventStoreDB, Cosmos change feed, or a Kafka-backed store were rejected because:

- Appending the event, updating the read model, and writing the outbox row must be atomic. PostgreSQL gives this for free in a single transaction; anything else requires two-phase commit or a saga.
- Same engine edge and cloud — the store-gateway sync story stays simple.
- No new sidecar to operate.
- Tooling options on .NET + PostgreSQL (Marten, or a thin DIY layer on EF Core with an append-only `OrderEvents` table) are mature.

**Tradeoffs accepted:**

- **Event schemas are load-bearing contracts.** Once written to the log, event shapes are forever. The `contracts` library owns versioned event schemas; breaking changes require new event types plus upcasters on the read side.
- **Ad-hoc "current state" queries** go through `OrderReadModel`, not the event log. Acceptable — the read model exists for exactly this.
- **Projections can drift.** A bug in a projection handler can leave `kds_db` or a Cosmos container inconsistent with the log. Mitigation: projections are rebuildable — a `RebuildProjection` admin command replays events into a fresh store.
- **Cross-domain events need a clear rule.** When `payment-api` emits `PaymentSucceeded` (a payment-domain event), does `order-processing-functions` append a corresponding `OrderPaymentConfirmed` to the order's stream, or do consumers of order state read both streams? Initial rule: cross-domain events trigger the ordering aggregate to append its own event — the order's stream is the complete narrative for that order. Revisit if it becomes onerous.

---

## Open Design Decisions

These gaps have been identified but not yet resolved. They should be addressed before or during implementation.

### Circuit Breaker / Retry Strategy

When a synchronous service-to-service call fails (e.g., ordering-api → payment-api), the system needs defined timeout, retry, and circuit breaker policies. Without these, a slow downstream service can cascade failures upstream. Needs a decision on:

- Timeout values per call type
- Retry policy (count, backoff strategy, idempotency requirements)
- Circuit breaker thresholds (failure rate, recovery probe interval)
- Library choice (likely Polly, which integrates with .NET's `HttpClientFactory`)

### Observability

With 13 services, 6 databases, and 2 deployment zones, debugging requires:

- **Distributed tracing** — A correlation ID that follows a request across services. The `common` library includes correlation ID propagation middleware, but the tracing backend (Application Insights, OpenTelemetry collector, Jaeger) is not decided.
- **Centralized logging** — Aggregation of logs from all services into a single queryable store.
- **Health dashboards** — Especially for edge services where store-gateway health directly impacts store operations.

### Data Retention and Cleanup

- **kds_db** holds "active orders" — needs a cleanup policy for completed orders (archive or delete after N hours/days)
- **ordering_db** accumulates full order history — needs a retention strategy at scale
- **store_db** outbox events are transient — should be purged after successful forwarding
- **Edge storage is limited** — retention policies are more aggressive at the edge than in the cloud

### Conflict Resolution (Per-Entity Strategy)

High-level rule: cloud state wins for menu/config, local state wins for orders created during outage. But edge cases need detailed resolution:

- **Menu item deleted in cloud while edge is offline, and edge accepts an order for that item** — Accept the order (customer expectation) but flag it for review on sync
- **Price changed in cloud, edge processes order at stale price** — Honor the price the customer saw (edge price at time of order)
- **Concurrent orders from cloud and edge reference the same inventory** — Last-write-wins with event timestamp ordering, or reservation-based model
- Per-entity resolution strategies should be documented in detail before implementing store-gateway sync logic
