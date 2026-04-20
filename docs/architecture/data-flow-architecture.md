# Distributed Online Ordering Platform — Data Flow Architecture

## Context

Design the data flow architecture for an online ordering platform demo that naturally utilizes all sidecar data containers in the dev environment. The goal is a coherent, realistic distributed system where each service plays a genuinely motivated role — not a contrived showcase. A localstripe Stripe emulator container will be added for payment processing.

The platform is split into multiple bounded-context services, each owning its own database and data library. This document maps the data flows onto those service boundaries.

> **Hybrid / Edge Deployment:** Several services (ordering-api, menu-api, payment-api, kds-api, order-processing-functions) are dual-deployed in cloud and edge (in-store). Edge instances use the outbox pattern for offline operation, syncing via the store-gateway when connectivity is restored. See `system-architecture.md` for the full deployment topology and offline resilience design.

---

## Sidecar Data Containers

| #   | Service                        | Protocol/Port           | Role in Architecture                               |
| --- | ------------------------------ | ----------------------- | -------------------------------------------------- |
| 1   | **PostgreSQL 17**              | TCP :5432               | Transactional system of record                     |
| 2   | **Redis 7**                    | TCP :6379               | Session state + hot-path cache                     |
| 3   | **Azure Service Bus**          | AMQP :5672              | Reliable command/task messaging                    |
| 4   | **Azurite** (Blob/Queue/Table) | HTTP :10000-10002       | File storage + audit log                           |
| 5   | **Azure Cosmos DB**            | HTTPS :8081             | Read-optimized query model (CQRS)                  |
| 6   | **Azure Event Hubs**           | Kafka :9092             | Event streaming / analytics                        |
| 7   | **Azure App Configuration**    | HTTP :8483              | Feature flags + dynamic config                     |
| 8   | **Mailpit**                    | SMTP :1025              | Transactional email                                |
| 9   | **localstripe**                | HTTP (TBD)              | Stripe payment emulator                            |
| 10  | **Keycloak**                   | HTTP :8080 (host :8180) | OIDC identity provider — APIs are resource servers |

---

## Domain Entities

### PostgreSQL (Normalized, Transactional) — Database per Bounded Context

**ordering_db** (via `ordering-data` lib — used by ordering-api, order-processing-functions):

The ordering domain is **event-sourced**. An order's state is an append-only log of domain events; the current state is a projection maintained in `OrderReadModel`. See `system-architecture.md` → Architectural Decision Records for the rationale.

```
OrderEvents     { Id (UUIDv7, event id),
                  AggregateId (UUIDv7, order id),
                  SequenceNumber (int, per-aggregate),
                  EventType,
                  Payload (jsonb),
                  OccurredAt,
                  CausationId, CorrelationId }
                 -- append-only; UNIQUE (AggregateId, SequenceNumber)

OrderReadModel  { AggregateId (PK), PublicOrderId, Status, UserId (OIDC sub),
                  CustomerEmail, CustomerName, TotalInCents, StripePaymentIntentId,
                  ItemsJson, LastSequenceApplied, CreatedAt, UpdatedAt }
                 -- rebuildable from OrderEvents
```

Event types: `OrderPlaced`, `OrderPaymentConfirmed`, `OrderConfirmed`, `OrderPreparing`, `OrderReady`, `OrderPickedUp`, `OrderCancelled`. Payloads are defined in the `contracts` library.

**menu_db** (via `menu-data` lib — used by menu-api exclusively):

```
MenuCategory    { Id, Name, SortOrder }
MenuItem        { Id, CategoryId, Name, Description, PriceInCents, ImageUrl, IsAvailable }
```

**payment_db** (via `payment-data` lib — used by payment-api exclusively, PCI-isolated):

```
PaymentIntent   { Id, OrderId, StripePaymentIntentId, AmountInCents, Status, CreatedAt }
PaymentEvent    { Id, PaymentIntentId, EventType, StripeEventId, Timestamp }
```

**admin_db** (via `admin-data` lib — used by admin-api exclusively):

```
AuditEntry      { Id, Action, ActorId, EntityType, EntityId, Details, Timestamp }
SystemConfig    { Key, Value, UpdatedAt }
```

**kds_db** (via `kds-data` lib — used by kds-api exclusively, event-populated):

```
KitchenOrder    { Id, PublicOrderId, CustomerName, Status, ReceivedAt, StartedAt, CompletedAt }
KitchenItem     { Id, KitchenOrderId, ItemName, Quantity, Notes, Station, Status }
```

**store_db** (via `store-data` lib — used by store-gateway exclusively, edge only):

```
OutboxEvent     { Id, EventType, Payload, CreatedAt, ForwardedAt, Status }
SyncCheckpoint  { Id, EntityType, LastSyncedAt, SequenceNumber }
ConnectivityLog { Id, Status (online/offline), Timestamp, DurationMs }
```

### Authentication (Keycloak OIDC)

```
All APIs: Resource servers — validate JWT Bearer tokens issued by Keycloak
  - Claims used: sub (userId), email, name, roles (admin vs. customer vs. kitchen)
  - admin-api endpoints require "admin" role claim
  - ordering-api customer endpoints (/cart, /checkout, /orders) require authenticated user
  - menu-api read (GET /menu) is public (no auth required)
  - kds-api endpoints require "kitchen" role claim

Frontends: OIDC clients — redirect to Keycloak for login, store tokens
  - Uses authorization code flow with PKCE
  - Tokens attached to API requests via Authorization header
```

### Cosmos DB (Denormalized, Read-Optimized) — 4 Containers

**Database: `ordering-platform`**

**Container 1: `KitchenTickets`** — CQRS read model for kitchen/fulfillment display

```json
{
  "id": "ORD-abc123",
  "partitionKey": "2026-04-10", // date-based — kitchen only cares about today
  "customerName": "Jane",
  "items": [
    { "name": "Fish Tacos", "qty": 2, "notes": "no cilantro" },
    { "name": "Lemonade", "qty": 1 }
  ],
  "status": "received | preparing | ready | picked-up",
  "placedAt": "2026-04-10T14:30:00Z",
  "updatedAt": "2026-04-10T14:31:00Z"
}
```

**Why:** Kitchen needs a flat, pre-joined document (customer + items + notes) — no SQL JOINs. Date partition means the kitchen view queries only today's orders efficiently. Status field is updated in-place as the order progresses.

**Container 2: `MenuCatalog`** — CQRS read model for customer-facing menu

```json
{
  "id": "category-entrees",
  "partitionKey": "menu", // single partition for the full catalog
  "categoryName": "Entrees",
  "sortOrder": 1,
  "items": [
    {
      "menuItemId": 1,
      "name": "Fish Tacos",
      "description": "Baja-style with lime crema",
      "priceInCents": 1250,
      "imageUrl": "http://azurite:10000/devstoreaccount1/menu-images/1.jpg",
      "isAvailable": true,
      "supportsSpecialInstructions": true
    }
  ]
}
```

**Why:** The menu read path serves denormalized documents — items pre-grouped by category with image URLs resolved. This avoids JOIN + GROUP BY on every menu page load. The `supportsSpecialInstructions` field is resolved from App Configuration at sync time. When an admin updates a menu item in PostgreSQL, a sync process rebuilds this read model.

**Write path:** PostgreSQL -> (sync event) -> Cosmos DB `MenuCatalog`
**Read path:** Redis cache -> Cosmos DB `MenuCatalog` (on cache miss)

**Container 3: `CustomerOrders`** — Order history per customer

```json
{
  "id": "user-a1b2c3d4", // Keycloak subject ID
  "partitionKey": "user-a1b2c3d4", // partition per user
  "customerName": "Jane Doe",
  "customerEmail": "jane@example.com",
  "orders": [
    {
      "publicOrderId": "ORD-abc123",
      "placedAt": "2026-04-10T14:30:00Z",
      "status": "picked-up",
      "totalInCents": 2450,
      "itemSummary": "Fish Tacos x2, Lemonade x1",
      "receiptUrl": "http://azurite:10000/devstoreaccount1/receipts/ORD-abc123.html"
    }
  ]
}
```

**Why:** "My Orders" page needs all orders for one customer with summary info — a single document read partitioned by OIDC subject ID. No pagination JOINs across Orders + LineItems + MenuItems. Append-only pattern: new orders are pushed to the array by Azure Functions when an order is placed.

**Container 4: `Analytics`** — Materialized views from Event Hubs stream

```json
// Hourly order volume
{
  "id": "orders-per-hour:2026-04-10T14",
  "partitionKey": "orders-per-hour",
  "hour": "2026-04-10T14:00:00Z",
  "count": 23,
  "totalRevenue": 45600
}

// Popular items (rolling)
{
  "id": "popular-items:2026-04-10",
  "partitionKey": "popular-items",
  "date": "2026-04-10",
  "items": [
    { "name": "Fish Tacos", "count": 47 },
    { "name": "Lemonade", "count": 35 }
  ]
}
```

**Why:** Event Hubs streams raw events but they're not queryable. An Azure Function consumes the stream and maintains pre-aggregated views in Cosmos DB. A dashboard page reads these directly — no expensive real-time aggregation. This gives Event Hubs a concrete consumer beyond just observability.

### Redis Keys

```
menu:catalog                 -> cached JSON of full menu catalog from Cosmos DB (TTL: 5 min)
cart:{userId}                -> hash of itemId -> { qty, name, price } (TTL: 2 hr)
order-status:{publicOrderId} -> cached status string (TTL: 30 sec)
idempotency:{key}            -> cached response body for retried POSTs (TTL: 24 hr)
                                keys: checkout requests, payment intent creation, outbox forwarding
ratelimit:{scope}:{id}       -> sliding-window counter (TTL: window size)
                                scopes: login attempts per user, checkout attempts per user,
                                payment-intent creations per order
projection:event-seen:{eventId} -> deduplication marker for projection handlers (TTL: 7 days)
```

**Idempotency keys** matter under event sourcing because the store-gateway retries forwarding events to cloud Event Hubs on network failure. The cloud-side projection handlers dedupe on event id via `projection:event-seen:{eventId}` before applying. The client-facing idempotency keys (`idempotency:{key}`) protect checkout and payment-intent creation from double-submission.

### Azurite Blob Storage

```
Container: menu-images
  +-- {menuItemId}.jpg       -> menu item photos (seeded, referenced by MenuCatalog docs)

Container: receipts
  +-- {publicOrderId}.html   -> generated HTML receipt document
```

### Azurite Table Storage

```
Table: OrderAudit
  PartitionKey: {publicOrderId}
  RowKey: {ISO timestamp}
  Action: "order-created" | "payment-confirmed" | "ticket-created" | "email-sent" | ...
  Details: free-text context
```

### App Configuration

```
FeatureManagement:EnableSpecialInstructions  -> bool    (toggles notes field on menu items)
FeatureManagement:EnableOrderTracking        -> bool    (toggles real-time status page vs. "we'll email you")
Menu:FeaturedCategoryId                      -> int     (which category to highlight)
Checkout:MaxItemsPerOrder                    -> int     (dynamic cart limit)
```

### Service Bus

```
Queue: order-placed          -> reliable delivery of order processing tasks
  Message: { orderId, publicOrderId, userId, customerEmail, customerName, items[], totalInCents }

Queue: menu-updated          -> triggers async rebuild of Cosmos DB MenuCatalog
  Message: { menuItemId?, categoryId?, action: "item-updated" | "category-updated" | "full-rebuild" }
```

### Event Hubs

```
Hub: order-events            -> append-only stream of order lifecycle events
  Event: { type, publicOrderId, timestamp, data }
  Types: order-placed, payment-confirmed, ticket-created, status-changed, order-completed
```

---

## End-to-End Data Flow

### 1. Menu Browsing (Read Path)

```
Browser -> GET /menu -> menu-api
                        |-> Redis: CHECK menu:catalog
                        |     |-- HIT -> return cached menu
                        |     +-- MISS --> Cosmos DB: READ MenuCatalog documents
                        |                  +-> Redis: SET menu:catalog (TTL 5m)
                        |-> App Configuration: READ feature flags
                        |     (EnableSpecialInstructions, FeaturedCategoryId)
                        +-> Response: { categories[{ items[] }], features{} }
```

**Why this three-tier read path (Redis -> Cosmos DB -> [PostgreSQL is write-only]):**

- **Redis** absorbs 99% of reads at sub-ms latency
- **Cosmos DB** serves cache misses with pre-denormalized documents (items grouped by category, image URLs resolved) — no JOINs
- **PostgreSQL** is never hit on the read path — it's the write-side of the CQRS split

**App Configuration** controls UI behavior without redeployment — toggle `EnableSpecialInstructions`, refresh the page, see the field appear/disappear.

### 1b. Menu Management (Write Path — Admin, requires Keycloak admin role)

```
admin-web -> admin-api -> menu-api (JWT validated via Keycloak, admin role required)
                          |-> menu_db: INSERT/UPDATE MenuItem (source of truth)
                          |-> Azurite Blob: UPLOAD menu-images/{menuItemId}.jpg (if image provided)
                          |-> Service Bus: SEND to queue "menu-updated"
                          |     Message: { menuItemId, action: "item-updated" }
                          +-> Event Hubs: PUBLISH { type: "menu-updated", ... }
```

```
Service Bus: "menu-updated" queue -> order-processing-functions: MenuCatalogSyncHandler
                                      |-> menu_db: SELECT MenuItems + Categories (full read)
                                      |-> App Configuration: READ feature flags (to resolve into catalog)
                                      |-> Cosmos DB [MenuCatalog]: REPLACE catalog documents
                                      |     (re-denormalize: group by category, resolve image URLs, apply flags)
                                      +-> Redis: DEL menu:catalog (invalidate cache)
```

**Why Service Bus for the sync:** The admin endpoint returns quickly — it only writes to menu_db (source of truth) and drops a message. The function handles the heavier work of rebuilding the denormalized Cosmos DB catalog. If the rebuild fails, Service Bus retries. This also means a second Service Bus queue (`menu-updated`), demonstrating the queue-per-concern pattern.

**Why menu_db stays the write source of truth:** Admin operations need ACID — updating a price must be atomic and consistent. The Cosmos DB catalog is a projection that can be fully rebuilt from menu_db at any time.

### 2. Cart Management (requires authenticated user)

```
ordering-web -> POST/DELETE/GET /cart/items -> ordering-api (userId from JWT sub claim)
                                               +-> Redis: HSET/HDEL/HGETALL cart:{userId}
```

**Why Redis only:** Cart is ephemeral, user-scoped, high-frequency (add/remove items). No durability needed — if Redis restarts, losing an uncommitted cart is acceptable. Sub-millisecond latency makes the UI feel instant. Using `userId` (from Keycloak JWT) instead of a session ID means the cart persists across devices for logged-in users.

### 3. Checkout (Synchronous, requires authenticated user)

```
ordering-web -> POST /checkout -> ordering-api (userId, email, name from JWT claims)
                                  |-> Redis: CHECK idempotency:{Idempotency-Key header}
                                  |     +-- HIT -> return cached response (safe retry)
                                  |-> Redis: HGETALL cart:{userId} (retrieve cart)
                                  |-> App Configuration: READ Checkout:MaxItemsPerOrder (validate)
                                  |-> ordering_db: BEGIN TRANSACTION
                                  |     APPEND OrderEvents: OrderPlaced
                                  |       { aggregateId, publicOrderId, userId, email, name,
                                  |         items[], totalInCents }
                                  |     UPSERT OrderReadModel (status: "pending-payment")
                                  |     INSERT OutboxEvent (one row per event to forward)
                                  |     COMMIT
                                  |-> payment-api: Create PaymentIntent (amount, metadata: { orderId })
                                  |     +-> localstripe: Create PaymentIntent via Stripe SDK
                                  |     +-> payment_db: INSERT PaymentIntent record
                                  |-> Azurite Table: INSERT OrderAudit (action: "order-created")
                                  |-> Redis: SET idempotency:{key} -> response (TTL 24h)
                                  +-> Response: { clientSecret, publicOrderId }

Outbox forwarder (background, in ordering-api or store-gateway at edge):
  -> ordering_db: SELECT OrderEvents WHERE outbox_forwarded = false ORDER BY SequenceNumber
  -> Event Hubs: PUBLISH { type: "OrderPlaced", aggregateId, sequence, payload, ... }
  -> ordering_db: UPDATE outbox row SET forwarded_at = now()
```

**Why event sourcing for the write:** Ordering is the one domain where edge and cloud both write concurrently during a network partition. Event sourcing turns reconciliation into log replay — edge-emitted events and cloud-emitted events are immutable facts that compose without conflict resolution. The append-event + update-read-model + insert-outbox-row transaction guarantees no event is published without being persisted (and vice versa). See `system-architecture.md` for the full rationale.

**Why payment-api is a separate call:** Payment processing is isolated in its own service with its own database (payment_db) for PCI compliance. ordering-api never touches Stripe directly. At the edge, payment-api uses store-and-forward to queue payments for cloud processing.

**Why Event Hubs is fed from the outbox (not directly from the endpoint):** Publishing to Event Hubs from inside the request handler introduces a dual-write problem — the DB commit can succeed and the Event Hubs publish can fail (or vice versa). Writing the event to `OrderEvents` atomically with the read-model update, then letting a background forwarder publish it, gives exactly-once-to-log semantics with at-least-once-to-subscribers. Consumers dedupe on event id.

### 4. Payment Confirmation (Webhook)

```
localstripe -> POST /webhooks/stripe -> payment-api
                                        |-> payment_db: UPDATE PaymentIntent SET Status = "succeeded"
                                        |-> Service Bus: SEND to queue "order-placed"
                                        |     Message: { orderId, publicOrderId, customerEmail, items[], ... }
                                        |-> Event Hubs: PUBLISH { type: "payment-confirmed", ... }
                                        |-> Azurite Table: INSERT OrderAudit (action: "payment-confirmed")
                                        +-> Redis: DEL cart:{userId} (clear the cart)
```

Note: ordering-api also updates the order status in ordering_db when it receives the "payment-confirmed" event (via order-processing-functions).

**Why Service Bus (not direct processing):** The webhook handler must return quickly. Downstream work (generating receipts, sending emails, creating kitchen tickets) is unreliable and slow. Service Bus provides at-least-once delivery with dead-letter queues — if processing crashes, the message is retried. This is a command ("process this order"), not a broadcast.

**Why both Service Bus AND Event Hubs:** Different semantics. Service Bus delivers a command to exactly one processor (order-processing-functions) with guaranteed processing. Event Hubs broadcasts a fact to any number of consumers (analytics, dashboards, notification-functions). They serve different purposes.

### 5. Order Processing (Asynchronous — order-processing-functions: OrderPlacedHandler)

```
Service Bus: "order-placed" queue -> order-processing-functions: OrderPlacedHandler
                                      |-> Redis: CHECK projection:event-seen:{eventId}
                                      |     +-- HIT -> skip (already processed)
                                      |-> ordering_db: BEGIN TRANSACTION
                                      |     APPEND OrderEvents: OrderPaymentConfirmed, OrderConfirmed
                                      |     UPSERT OrderReadModel (status: "confirmed")
                                      |     INSERT OutboxEvent (for downstream Event Hubs broadcast)
                                      |     COMMIT
                                      |-> Cosmos DB [KitchenTickets]: INSERT ticket document (projection)
                                      |     (denormalized: customer name, item names, qty, status: "received")
                                      |-> Cosmos DB [CustomerOrders]: UPSERT — append order to customer's history (projection)
                                      |     (publicOrderId, item summary, total, status, receipt URL)
                                      |-> Azurite Blob: UPLOAD receipts/{publicOrderId}.html
                                      |     (generated HTML receipt with order details)
                                      |-> Azurite Table: INSERT OrderAudit entries for each step
                                      |-> Redis: SET order-status:{publicOrderId} = "received" (TTL 30s)
                                      +-> Redis: SET projection:event-seen:{eventId} (TTL 7 days)
```

```
Event Hubs: "order-confirmed" -> notification-functions: OrderConfirmationHandler
                                  +-> Mailpit: SEND order confirmation email (SMTP)
                                       (to customerEmail, includes order summary + receipt link)
```

```
Event Hubs: "order-confirmed" -> kds-api (event consumer)
                                  +-> kds_db: INSERT KitchenOrder + KitchenItems
                                       (populates the KDS read model from the event payload)
```

**Why kds_db is event-populated:** The KDS has its own database populated via events, not by sharing the ordering database. This means the KDS can operate offline with its last-received orders, holds only active order data (no customer PII or payment info), and can evolve its schema independently. See `system-architecture.md` for the full KDS data flow.

**Why Cosmos DB for kitchen tickets:** The Cosmos DB KitchenTickets container remains as a CQRS read model for cloud-side queries (e.g., analytics, admin dashboards). The edge KDS reads from kds_db for its local operations.

**Why Cosmos DB for customer order history:** "My Orders" page needs all orders for one customer in one read. Appending to a customer-partitioned document avoids paginated JOINs across Orders + LineItems. Written once here, read many times by the customer.

**Why Azurite Blob for receipts:** Receipts are immutable files. Blob storage is cheaper than a database for large objects, supports direct URL access, and is CDN-friendly in production.

**Why Azurite Table for audit:** Write-heavy, read-rarely, schemaless, append-only. Table Storage is cost-effective for this pattern and doesn't need relational queries.

### 5b. Analytics Materialization (Asynchronous — order-processing-functions: AnalyticsProcessor)

```
Event Hubs: "order-events" stream -> order-processing-functions: AnalyticsProcessor
                                       |-> Cosmos DB [Analytics]: UPSERT orders-per-hour:{hour}
                                       |     (increment count, add to revenue total)
                                       |-> Cosmos DB [Analytics]: UPSERT popular-items:{date}
                                       |     (increment item counts, re-sort top items)
                                       +-> Azurite Table: INSERT OrderAudit (action: "analytics-updated")
```

**Why a separate Event Hubs consumer:** This decouples analytics from order processing. The OrderPlacedHandler (Service Bus consumer) handles the critical path — kitchen ticket, receipt, status update. The AnalyticsProcessor (Event Hubs consumer) handles non-critical aggregation independently. If analytics falls behind, it doesn't block order fulfillment. Event Hubs' consumer group model means both can run in parallel without interference.

**Why Cosmos DB for analytics:** Pre-aggregated materialized views are cheaper to read than computing aggregations on every dashboard refresh. The dashboard page reads a single document per metric. Cosmos DB's UPSERT with partial updates makes increment operations simple.

### 6. Order Status Updates (Kitchen -> Customer)

```
kds-web -> POST /tickets/{id}/status -> kds-api
                                        |-> kds_db: UPDATE KitchenOrder.Status
                                        |-> Event Hubs: PUBLISH { type: "status-changed", ... }
                                        |     (consumed by order-processing-functions and notification-functions)
                                        |-> Redis: SET order-status:{publicOrderId} (TTL 30s)
                                        +-> Azurite Table: INSERT OrderAudit
```

```
Event Hubs: "status-changed" -> order-processing-functions
                                 +-> ordering_db: APPEND OrderEvents (OrderPreparing | OrderReady | OrderPickedUp)
                                 |                + UPSERT OrderReadModel.Status (one transaction)
                                 +-> Cosmos DB [KitchenTickets]: UPDATE status (projection)
```

```
Event Hubs: "status-changed" [if status == "ready"] -> notification-functions
                                                         +-> Mailpit: SEND pickup notification email
```

### 7. Order Status Polling (Customer)

```
ordering-web -> GET /orders/{publicOrderId}/status -> ordering-api
                                                       |-> Redis: CHECK order-status:{publicOrderId}
                                                       |     |-- HIT -> return cached status
                                                       |     +-- MISS --> Cosmos DB: READ KitchenTicket
                                                       |                   +-> Redis: SET (TTL 30s)
                                                       |-> App Configuration: READ EnableOrderTracking
                                                       |     (determines whether to return full timeline or just "we'll email you")
                                                       +-> Response: { status, timeline?, receiptUrl? }
```

**Why Cosmos DB (not ordering_db) for status reads:** The status page needs the same denormalized shape as the kitchen view — items with notes, timeline. Reading from Cosmos DB avoids JOINs and keeps read traffic off the transactional database. Redis sits in front for the highest-frequency polling (30s TTL balances freshness vs. load).

### 8. Receipt Download

```
ordering-web -> GET /orders/{publicOrderId}/receipt -> ordering-api
                                                        +-> Azurite Blob: GET receipts/{publicOrderId}.html
                                                              +-> Response: HTML receipt (or redirect to blob URL)
```

### 9. Customer Order History (requires authenticated user)

```
ordering-web -> GET /orders/history -> ordering-api (userId from JWT sub claim)
                                       +-> Cosmos DB [CustomerOrders]: READ by partition key (userId)
                                             +-> Response: { orders[] } — single document, no pagination JOINs
```

### 10. Analytics Dashboard

```
admin-web -> GET /analytics/summary -> admin-api
                                       |-> Cosmos DB [Analytics]: READ orders-per-hour (last 24h)
                                       |-> Cosmos DB [Analytics]: READ popular-items (today)
                                       +-> Response: { ordersPerHour[], popularItems[] }
```

---

## Architecture Diagram

```
+------------------------------------------------------------------------------+
|                         SYNCHRONOUS PATH (APIs)                              |
|                                                                              |
|  ordering-web/mobile <--> ordering-api                                       |
|                            |-> ordering_db (write orders)                    |
|                            |-> payment-api -> localstripe, payment_db        |
|                            |-> menu-api -> menu_db, Redis, Cosmos DB         |
|                            |-> Redis (cache, cart)                            |
|                            +-> App Config (feature flags)                    |
|                                                                              |
|  admin-web <--> admin-api                                                    |
|                  |-> menu-api -> menu_db (menu management)                   |
|                  |-> Cosmos DB [Analytics] (dashboards)                      |
|                  +-> admin_db (audit, config)                                |
|                                                                              |
|  kds-web <--> kds-api                                                        |
|                |-> kds_db (order queue, status updates)                      |
|                +-> Event Hubs (publish status changes)                       |
+------------------------------------------------------------------------------+
                  |                                               ^
                  | Service Bus (async command)                   | Event Hubs (broadcast)
                  v                                               |
+------------------------------------------------------------------------------+
|                     ASYNCHRONOUS PATH                                        |
|                                                                              |
|  order-processing-functions:                                                 |
|  +- OrderPlacedHandler (Service Bus trigger) -------------------------+      |
|  |   |--> ordering_db (update order status)                           |      |
|  |   |--> Cosmos DB [KitchenTickets] (write kitchen ticket)           |      |
|  |   |--> Cosmos DB [CustomerOrders] (append to order history)        |      |
|  |   |--> Azurite Blob (upload receipt HTML)                          |      |
|  |   |--> Azurite Table (audit log entries)                           |      |
|  |   +--> Event Hubs (publish order-confirmed) ---------------+      |      |
|  +------------------------------------------------------------+------+      |
|                                                                |             |
|  +- MenuCatalogSyncHandler (Service Bus trigger) ----------+  |             |
|  |   |--> menu_db (read menu items)                         |  |             |
|  |   |--> App Configuration (read feature flags)            |  |             |
|  |   |--> Cosmos DB [MenuCatalog] (rebuild catalog)         |  |             |
|  |   +--> Redis (invalidate cache)                          |  |             |
|  +----------------------------------------------------------+  |             |
|                                                                |             |
|  +- AnalyticsProcessor (Event Hubs trigger) <---------+       |             |
|  |   |--> Cosmos DB [Analytics] (upsert views)        |       |             |
|  |   +--> Azurite Table (audit log)                    |       |             |
|  +-----------------------------------------------------+       |             |
|                                                                |             |
|  notification-functions:                                       |             |
|  +- OrderConfirmationHandler (Event Hubs trigger) <-----------+             |
|  |   +--> Mailpit (send confirmation email)                                  |
|  +- PickupNotificationHandler (Event Hubs trigger)                           |
|  |   +--> Mailpit (send pickup ready email)                                  |
|  +-----------------------------------------------------------------------+   |
|                                                                              |
|  kds-api (Event Hubs consumer):                                              |
|  +- Consumes "order-confirmed" events                                        |
|  |   +--> kds_db: INSERT KitchenOrder + KitchenItems                         |
|  +-----------------------------------------------------------------------+   |
+------------------------------------------------------------------------------+
```

### Data Store Responsibility Matrix

```
                         ordering_db  menu_db  payment_db  kds_db  admin_db  Cosmos DB   Redis   Azurite    Service Bus   Event Hubs  App Config  Mailpit  Keycloak
                         -----------  -------  ----------  ------  --------  ---------   -----   -------    -----------   ----------  ----------  -------  --------
Service: ordering-api
  Auth                                                                                                                                                     JWT
  Cart                                                                                           store
  Checkout                    W                                                                              Pub           Read                             user
  Order status                                                                Read       Cache                                        Read
  Customer history                                                            Read                                                                         user
  Receipt download                                                                                Blob

Service: payment-api
  Payment intent                          W                                                      order-snd   Pub
  Stripe webhook                          W                       Del        Table

Service: menu-api
  Menu read                                                                   Catalog    cache                                        Read
  Menu write                      W                                                      Blob    menu-upd    Pub                                           admin

Service: admin-api
  Analytics                                                        W         Agg
  Admin config                                                     W

Service: kds-api
  Kitchen display                                        RW                                                   Pub
  Event consumer                                         W                                                                Recv

Service: order-processing-functions
  OrderPlacedHandler      W                                                   Ticket             order-recv   Pub
                                                                              History    Set     Blob/Table
  MenuCatalogSync                  Read                                       Catalog                         menu-recv    Read
  AnalyticsProcessor                                                          Agg                                          Recv

Service: notification-functions
  Order confirmation                                                                                                       Recv                  Send
  Pickup notification                                                                                                      Recv                  Send

Service: store-gateway                                                                    store_db
  Outbox forwarding                                                              RW                  Snd        Pub
  Sync checkpoints                                                               RW
  Connectivity monitoring                                                        W
```

---

## Key Architectural Patterns Demonstrated

1. **Event Sourcing (ordering domain)** — Order state is an append-only log of events (`OrderEvents`) with a projection (`OrderReadModel`) for fast reads. All other ordering-shaped data stores (kds_db, Cosmos KitchenTickets, Cosmos CustomerOrders, Cosmos Analytics) are projections of the same event stream. See `system-architecture.md` ADR.
2. **CQRS (two instances)**
   - Orders: `ordering_db` event log (write) -> `OrderReadModel` + Cosmos `KitchenTickets` + Cosmos `CustomerOrders` + `kds_db` (all projections)
   - Menu: `menu_db` (write/admin) -> Cosmos DB `MenuCatalog` (read/customer)
3. **Database-per-Bounded-Context** — Each domain (ordering, menu, payment, admin, KDS) has its own PostgreSQL database. Services communicate via APIs or events, not shared databases.
4. **Cache-Aside** — Redis in front of Cosmos DB for both menu and order status.
5. **Idempotent Retries** — Redis-backed idempotency keys for client POSTs (checkout, payment intent) and event-id dedup markers for projection handlers.
6. **Async Command Processing** — Service Bus queue decouples Stripe webhook from heavy downstream work.
7. **Event Streaming + Materialized Views** — Event Hubs broadcasts lifecycle events; AnalyticsProcessor consumes them into pre-aggregated Cosmos DB views.
8. **Transactional Outbox (collapsed into the event log)** — The event-sourced `OrderEvents` table is itself the outbox. A forwarder reads unsent events by `SequenceNumber` and publishes to Event Hubs atomically-once-from-source.
9. **Feature Flags** — App Configuration controls runtime behavior (UI fields, tracking mode, cart limits) without redeployment.
10. **Saga/Orchestration** — `OrderPlacedHandler` coordinates multiple side effects from a single Service Bus trigger.
11. **Blob Storage for Static Assets** — Menu images and receipts in Azurite, referenced by URL from Cosmos DB and API responses.
12. **Append-Only Audit Trail** — Azurite Table Storage captures every processing step as immutable records (in addition to the event log itself, which is inherently audit-ready for the ordering domain).
13. **Edge/Cloud Log Replay** — Because ordering is event-sourced, edge-originated and cloud-originated events compose without conflict resolution — the store-gateway forwards events and the cloud projects them in arrival order.

---

## Verification / How to Observe Each Service in Action

| Service           | What to Look At                                                                            | Where                                                    |
| ----------------- | ------------------------------------------------------------------------------------------ | -------------------------------------------------------- |
| PostgreSQL        | `SELECT * FROM "Orders"` and `"MenuItems"` after operations                                | VS Code PostgreSQL extension                             |
| Redis             | `menu:catalog`, `cart:*`, `order-status:*` keys                                            | VS Code Redis extension                                  |
| Service Bus       | Messages arriving/consumed on `order-placed` and `menu-updated` queues                     | EventHub Explorer :5235 or VS Code Service Bus extension |
| Azurite Blob      | `menu-images/` container (seeded) + `receipts/` (generated)                                | VS Code Azure Storage extension                          |
| Azurite Table     | `OrderAudit` rows with timestamps and actions                                              | VS Code Azure Storage extension                          |
| Cosmos DB         | 4 containers: KitchenTickets, MenuCatalog, CustomerOrders, Analytics                       | VS Code Cosmos DB extension                              |
| Event Hubs        | Live stream of order lifecycle events (order-placed, payment-confirmed, status-changed...) | EventHub Explorer :5235                                  |
| App Configuration | Toggle `EnableSpecialInstructions` -> refresh page -> field appears/disappears             | curl + browser                                           |
| Mailpit           | Order confirmation and pickup notification emails with HTML rendering                      | Web UI at :8025                                          |
| localstripe       | Payment intent creation and webhook delivery                                               | Container logs                                           |
| Keycloak          | JWT token issuance, user authentication, role-based access                                 | Admin console at :8180 (admin/admin)                     |

---

## Resolved Design Decisions

1. **Menu sync trigger:** Service Bus async — admin endpoint writes to PostgreSQL and sends a `menu-updated` message. A `MenuCatalogSyncHandler` Azure Function rebuilds the Cosmos DB catalog. This adds a second queue (`menu-updated`) and demonstrates the queue-per-concern pattern.

2. **Customer identity:** Keycloak OIDC sidecar — WebAPI is a resource server validating JWT Bearer tokens. Customer identity comes from the `sub` claim. Role-based access (admin vs. customer) via Keycloak role claims. Frontend uses authorization code flow with PKCE.

3. **Seed data strategy:** Deferred — to be decided later.

## Open Design Questions

1. **Keycloak realm configuration:** How should the Keycloak realm be pre-configured? Options: (a) realm export JSON mounted as a volume, (b) Keycloak admin API calls in a startup script. The realm needs clients for each frontend (ordering-web, admin-web, kds-web), three roles (admin, customer, kitchen), and test users per role.

2. **Seed data strategy:** How should demo data (menu items, sample orders) be seeded across the multiple databases?

3. **Edge database engine:** Should edge stores use PostgreSQL (matching cloud) or a lighter option like SQLite for simpler deployment?

## Resolved Design Questions

1. **Admin UI scope:** Resolved — `admin-web` is a separate frontend app with `admin-api` as its dedicated backend. The admin endpoints are not in the ordering API.

2. **Azure Functions split:** Resolved — Split into two projects: `order-processing-functions` (OrderPlacedHandler, MenuCatalogSyncHandler, AnalyticsProcessor) and `notification-functions` (email, push, SMS notifications).

3. **Database separation:** Resolved — Database-per-bounded-context with five databases (ordering_db, menu_db, payment_db, admin_db, kds_db) on a shared PostgreSQL instance. See `system-architecture.md`.

4. **KDS data access:** Resolved — kds_db is an event-driven read model populated via events from order-processing-functions, not by sharing the ordering database. Enables offline operation and data isolation.
