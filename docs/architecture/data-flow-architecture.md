# Distributed Online Ordering Platform — Data Flow Architecture

## Context

Design the data flow architecture for an online ordering platform demo that naturally utilizes all sidecar data containers in the dev environment. The goal is a coherent, realistic distributed system where each service plays a genuinely motivated role — not a contrived showcase. A localstripe Stripe emulator container will be added for payment processing.

---

## Sidecar Data Containers

| #   | Service                        | Protocol/Port               | Role in Architecture                                 |
| --- | ------------------------------ | --------------------------- | ---------------------------------------------------- |
| 1   | **PostgreSQL 17**              | TCP :5432                   | Transactional system of record                       |
| 2   | **Redis 7**                    | TCP :6379                   | Session state + hot-path cache                       |
| 3   | **Azure Service Bus**          | AMQP :5672                  | Reliable command/task messaging                      |
| 4   | **Azurite** (Blob/Queue/Table) | HTTP :10000-10002           | File storage + audit log                             |
| 5   | **Azure Cosmos DB**            | HTTPS :8081                 | Read-optimized query model (CQRS)                    |
| 6   | **Azure Event Hubs**           | Kafka :9092                 | Event streaming / analytics                          |
| 7   | **Azure App Configuration**    | HTTP :8483                  | Feature flags + dynamic config                       |
| 8   | **Mailpit**                    | SMTP :1025                  | Transactional email                                  |
| 9   | **localstripe**                | HTTP (TBD)                  | Stripe payment emulator                              |
| 10  | **Keycloak**                   | HTTP (TBD, typically :8080) | OIDC identity provider — WebAPI is a resource server |

---

## Domain Entities

### PostgreSQL (Normalized, Transactional)

```
MenuCategory    { Id, Name, SortOrder }
MenuItem        { Id, CategoryId, Name, Description, PriceInCents, ImageUrl, IsAvailable }
Order           { Id, PublicOrderId, Status, UserId (OIDC sub), CustomerEmail, CustomerName,
                  TotalInCents, StripePaymentIntentId, CreatedAt, UpdatedAt }
OrderLineItem   { Id, OrderId, MenuItemId, ItemName, Quantity, UnitPriceInCents }
```

### Authentication (Keycloak OIDC)

```
WebAPI: Resource server — validates JWT Bearer tokens issued by Keycloak
  - Claims used: sub (userId), email, name, roles (admin vs. customer)
  - Admin endpoints (/admin/*) require "admin" role claim
  - Customer endpoints (/cart, /checkout, /orders) require authenticated user
  - Menu read (GET /menu) is public (no auth required)

Frontend: OIDC client — redirects to Keycloak for login, stores tokens
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
menu:catalog                -> cached JSON of full menu catalog from Cosmos DB (TTL: 5 min)
cart:{userId}               -> hash of itemId -> { qty, name, price } (TTL: 2 hr)
order-status:{publicOrderId} -> cached status string (TTL: 30 sec)
```

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
Browser -> GET /menu -> WebAPI
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
Admin -> POST/PUT /admin/menu -> WebAPI (JWT validated via Keycloak)
                                 |-> PostgreSQL: INSERT/UPDATE MenuItem (source of truth)
                                 |-> Azurite Blob: UPLOAD menu-images/{menuItemId}.jpg (if image provided)
                                 |-> Service Bus: SEND to queue "menu-updated"
                                 |     Message: { menuItemId, action: "item-updated" }
                                 +-> Event Hubs: PUBLISH { type: "menu-updated", ... }
```

```
Service Bus: "menu-updated" queue -> Azure Function: MenuCatalogSyncHandler
                                      |-> PostgreSQL: SELECT MenuItems + Categories (full read)
                                      |-> App Configuration: READ feature flags (to resolve into catalog)
                                      |-> Cosmos DB [MenuCatalog]: REPLACE catalog documents
                                      |     (re-denormalize: group by category, resolve image URLs, apply flags)
                                      +-> Redis: DEL menu:catalog (invalidate cache)
```

**Why Service Bus for the sync:** The admin endpoint returns quickly — it only writes to PostgreSQL (source of truth) and drops a message. The Azure Function handles the heavier work of rebuilding the denormalized Cosmos DB catalog. If the rebuild fails, Service Bus retries. This also means a second Service Bus queue (`menu-updated`), demonstrating the queue-per-concern pattern.

**Why PostgreSQL stays the write source of truth:** Admin operations need ACID — updating a price must be atomic and consistent. The Cosmos DB catalog is a projection that can be fully rebuilt from PostgreSQL at any time.

### 2. Cart Management (requires authenticated user)

```
Browser -> POST/DELETE/GET /cart/items -> WebAPI (userId from JWT sub claim)
                                          +-> Redis: HSET/HDEL/HGETALL cart:{userId}
```

**Why Redis only:** Cart is ephemeral, user-scoped, high-frequency (add/remove items). No durability needed — if Redis restarts, losing an uncommitted cart is acceptable. Sub-millisecond latency makes the UI feel instant. Using `userId` (from Keycloak JWT) instead of a session ID means the cart persists across devices for logged-in users.

### 3. Checkout (Synchronous, requires authenticated user)

```
Browser -> POST /checkout -> WebAPI (userId, email, name from JWT claims)
                            |-> Redis: HGETALL cart:{userId} (retrieve cart)
                            |-> App Configuration: READ Checkout:MaxItemsPerOrder (validate)
                            |-> PostgreSQL: BEGIN TRANSACTION
                            |     INSERT Order (status: "pending-payment", UserId from JWT sub)
                            |     INSERT OrderLineItems
                            |     COMMIT
                            |-> localstripe: Create PaymentIntent (amount, metadata: { orderId })
                            |-> Azurite Table: INSERT OrderAudit (action: "order-created")
                            |-> Event Hubs: PUBLISH { type: "order-placed", ... }
                            +-> Response: { clientSecret, publicOrderId }
```

**Why PostgreSQL for the write:** This is a transaction that must be atomic — the order and its line items either both exist or neither does. Relational integrity (line item references valid order and menu item) matters here. This is the system of record.

**Why Event Hubs here:** The "order placed" event is a fact that multiple consumers may care about (analytics, dashboards). Event Hubs is for broadcast; it doesn't care who reads it.

### 4. Payment Confirmation (Webhook)

```
localstripe -> POST /webhooks/stripe -> WebAPI
                                        |-> PostgreSQL: UPDATE Order SET Status = "paid"
                                        |-> Service Bus: SEND to queue "order-placed"
                                        |     Message: { orderId, publicOrderId, customerEmail, items[], ... }
                                        |-> Event Hubs: PUBLISH { type: "payment-confirmed", ... }
                                        |-> Azurite Table: INSERT OrderAudit (action: "payment-confirmed")
                                        +-> Redis: DEL cart:{userId} (clear the cart)
```

**Why Service Bus (not direct processing):** The webhook handler must return quickly. Downstream work (generating receipts, sending emails, creating kitchen tickets) is unreliable and slow. Service Bus provides at-least-once delivery with dead-letter queues — if processing crashes, the message is retried. This is a command ("process this order"), not a broadcast.

**Why both Service Bus AND Event Hubs:** Different semantics. Service Bus delivers a command to exactly one processor (Azure Function) with guaranteed processing. Event Hubs broadcasts a fact to any number of consumers (analytics, dashboards, future ML pipeline). They serve different purposes.

### 5. Order Processing (Asynchronous — Azure Function: OrderPlacedHandler)

```
Service Bus: "order-placed" queue -> Azure Function: OrderPlacedHandler
                                      |-> Cosmos DB [KitchenTickets]: INSERT ticket document
                                      |     (denormalized: customer name, item names, qty, status: "received")
                                      |-> Cosmos DB [CustomerOrders]: UPSERT — append order to customer's history
                                      |     (publicOrderId, item summary, total, status, receipt URL)
                                      |-> Azurite Blob: UPLOAD receipts/{publicOrderId}.html
                                      |     (generated HTML receipt with order details)
                                      |-> Mailpit: SEND order confirmation email (SMTP)
                                      |     (to customerEmail, includes order summary + receipt link)
                                      |-> Event Hubs: PUBLISH { type: "order-confirmed", ... }
                                      |-> Azurite Table: INSERT OrderAudit entries for each step
                                      +-> Redis: SET order-status:{publicOrderId} = "received" (TTL 30s)
```

**Why Cosmos DB for kitchen tickets:** The kitchen needs a flat, pre-joined document (customer + items + notes) — not a relational JOIN. Date-partitioned so kitchen view queries only today. This is a CQRS read model: PostgreSQL is the write model (transactional), Cosmos DB is the read model (query-optimized).

**Why Cosmos DB for customer order history:** "My Orders" page needs all orders for one customer in one read. Appending to a customer-partitioned document avoids paginated JOINs across Orders + LineItems. Written once here, read many times by the customer.

**Why Azurite Blob for receipts:** Receipts are immutable files. Blob storage is cheaper than a database for large objects, supports direct URL access, and is CDN-friendly in production.

**Why Azurite Table for audit:** Write-heavy, read-rarely, schemaless, append-only. Table Storage is cost-effective for this pattern and doesn't need relational queries.

### 5b. Analytics Materialization (Asynchronous — Azure Function: AnalyticsProcessor)

```
Event Hubs: "order-events" stream -> Azure Function: AnalyticsProcessor
                                       |-> Cosmos DB [Analytics]: UPSERT orders-per-hour:{hour}
                                       |     (increment count, add to revenue total)
                                       |-> Cosmos DB [Analytics]: UPSERT popular-items:{date}
                                       |     (increment item counts, re-sort top items)
                                       +-> Azurite Table: INSERT OrderAudit (action: "analytics-updated")
```

**Why a separate Event Hubs consumer:** This decouples analytics from order processing. The OrderPlacedHandler (Service Bus consumer) handles the critical path — kitchen ticket, receipt, email. The AnalyticsProcessor (Event Hubs consumer) handles non-critical aggregation independently. If analytics falls behind, it doesn't block order fulfillment. Event Hubs' consumer group model means both can run in parallel without interference.

**Why Cosmos DB for analytics:** Pre-aggregated materialized views are cheaper to read than computing aggregations on every dashboard refresh. The dashboard page reads a single document per metric. Cosmos DB's UPSERT with partial updates makes increment operations simple.

### 6. Order Status Updates (Kitchen -> Customer)

```
Kitchen UI -> POST /kitchen/tickets/{id}/status -> WebAPI
                                                    |-> Cosmos DB: UPDATE KitchenTicket.status
                                                    |-> Event Hubs: PUBLISH { type: "status-changed", ... }
                                                    |-> Redis: SET order-status:{publicOrderId} (TTL 30s)
                                                    |-> Azurite Table: INSERT OrderAudit
                                                    +-> [if status == "ready"]:
                                                          Mailpit: SEND pickup notification email
```

### 7. Order Status Polling (Customer)

```
Browser -> GET /orders/{publicOrderId}/status -> WebAPI
                                                 |-> Redis: CHECK order-status:{publicOrderId}
                                                 |     |-- HIT -> return cached status
                                                 |     +-- MISS --> Cosmos DB: READ KitchenTicket
                                                 |                   +-> Redis: SET (TTL 30s)
                                                 |-> App Configuration: READ EnableOrderTracking
                                                 |     (determines whether to return full timeline or just "we'll email you")
                                                 +-> Response: { status, timeline?, receiptUrl? }
```

**Why Cosmos DB (not PostgreSQL) for status reads:** The status page needs the same denormalized shape as the kitchen view — items with notes, timeline. Reading from Cosmos DB avoids JOINs and keeps read traffic off the transactional database. Redis sits in front for the highest-frequency polling (30s TTL balances freshness vs. load).

### 8. Receipt Download

```
Browser -> GET /orders/{publicOrderId}/receipt -> WebAPI
                                                  +-> Azurite Blob: GET receipts/{publicOrderId}.html
                                                        +-> Response: HTML receipt (or redirect to blob URL)
```

### 9. Customer Order History (requires authenticated user)

```
Browser -> GET /orders/history -> WebAPI (userId from JWT sub claim)
                                  +-> Cosmos DB [CustomerOrders]: READ by partition key (userId)
                                        +-> Response: { orders[] } — single document, no pagination JOINs
```

### 10. Analytics Dashboard

```
Browser -> GET /analytics/summary -> WebAPI
                                     |-> Cosmos DB [Analytics]: READ orders-per-hour (last 24h)
                                     |-> Cosmos DB [Analytics]: READ popular-items (today)
                                     +-> Response: { ordersPerHour[], popularItems[] }
```

---

## Architecture Diagram

```
+------------------------------------------------------------------------------+
|                         SYNCHRONOUS PATH (WebAPI)                            |
|                                                                              |
|  Browser <--> WebAPI                                                         |
|                 |                                                             |
|   +-------------+----------+--------------+--------------+---------+         |
|   v             v          v              v              v         v         |
| PostgreSQL    Redis     Cosmos DB    App Config    localstripe  Azurite      |
| (writes:     (cache:    (reads:      (feature      (payment    (Table:      |
|  orders,      menu,      menu catalog, flags,       intents)    audit;      |
|  menu CRUD)   cart,      order status, dynamic                  Blob:       |
|               status)    history,      config)                  images)     |
|                          analytics)                                         |
+------------------------------------------------------------------------------+
                  |                                               ^
                  | Service Bus (async command)                   | Event Hubs (broadcast)
                  v                                               |
+------------------------------------------------------------------------------+
|                     ASYNCHRONOUS PATH (Azure Functions)                       |
|                                                                              |
|  +- OrderPlacedHandler (Service Bus trigger) -------------------------+      |
|  |   |--> Cosmos DB [KitchenTickets] (write kitchen ticket)           |      |
|  |   |--> Cosmos DB [CustomerOrders] (append to order history)        |      |
|  |   |--> Azurite Blob (upload receipt HTML)                          |      |
|  |   |--> Azurite Table (audit log entries)                           |      |
|  |   |--> Mailpit (send confirmation email)                           |      |
|  |   +--> Event Hubs (publish order-confirmed event) ----------+      |      |
|  +-------------------------------------------------------------+------+      |
|                                                                 |            |
|  +- MenuCatalogSyncHandler (Service Bus trigger) ----------+   |            |
|  |   |--> PostgreSQL (read menu items)                      |   |            |
|  |   |--> App Configuration (read feature flags)            |   |            |
|  |   |--> Cosmos DB [MenuCatalog] (rebuild catalog)         |   |            |
|  |   +--> Redis (invalidate cache)                          |   |            |
|  +----------------------------------------------------------+   |            |
|                                                                 |            |
|  +- AnalyticsProcessor (Event Hubs trigger) <-------------------+            |
|  |   |--> Cosmos DB [Analytics] (upsert materialized views)                  |
|  |   +--> Azurite Table (audit log)                                          |
|  +-----------------------------------------------------------------------+   |
+------------------------------------------------------------------------------+
```

### Data Store Responsibility Matrix

```
                    PostgreSQL  Cosmos DB   Redis   Azurite    Service Bus   Event Hubs  App Config  Mailpit  Keycloak
                    ----------  ---------   -----   -------    -----------   ----------  ----------  -------  --------
Auth (all endpoints)                                                                                          JWT
Menu (admin write)      W                           Blob       menu-upd      Pub                              admin
Menu (catalog sync)     Read     Catalog                       menu-recv                 Read
Menu (cache)                                cache
Cart                                        store                                                             user
Checkout (order)        W                                                    Pub          Read                user
Payment webhook         W                   Del     Table      order-snd     Pub
Order processing                 Ticket             Blob       order-recv    Pub                     Send
                                 History    Set     Table
Analytics                        Agg                                         Recv
Order status                     Read       Cache                                        Read
Customer history                 Read                                                                         user
Kitchen status                   Write                                       Pub                     Send     admin
Audit trail                                         Table
```

---

## Key Architectural Patterns Demonstrated

1. **CQRS (two instances)**
   - Orders: PostgreSQL (write) -> Cosmos DB KitchenTickets + CustomerOrders (read)
   - Menu: PostgreSQL (write/admin) -> Cosmos DB MenuCatalog (read/customer)
2. **Cache-Aside** — Redis in front of Cosmos DB for both menu and order status
3. **Async Command Processing** — Service Bus queue decouples Stripe webhook from heavy downstream work
4. **Event Streaming + Materialized Views** — Event Hubs broadcasts lifecycle events; AnalyticsProcessor consumes them into pre-aggregated Cosmos DB views
5. **Feature Flags** — App Configuration controls runtime behavior (UI fields, tracking mode, cart limits) without redeployment
6. **Saga/Orchestration** — OrderPlacedHandler coordinates 6 side effects from a single Service Bus trigger
7. **Blob Storage for Static Assets** — Menu images and receipts in Azurite, referenced by URL from Cosmos DB and API responses
8. **Append-Only Audit Trail** — Azurite Table Storage captures every processing step as immutable records

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

---

## Resolved Design Decisions

1. **Menu sync trigger:** Service Bus async — admin endpoint writes to PostgreSQL and sends a `menu-updated` message. A `MenuCatalogSyncHandler` Azure Function rebuilds the Cosmos DB catalog. This adds a second queue (`menu-updated`) and demonstrates the queue-per-concern pattern.

2. **Customer identity:** Keycloak OIDC sidecar — WebAPI is a resource server validating JWT Bearer tokens. Customer identity comes from the `sub` claim. Role-based access (admin vs. customer) via Keycloak role claims. Frontend uses authorization code flow with PKCE.

3. **Seed data strategy:** Deferred — to be decided later.

## Open Design Questions

1. **Keycloak realm configuration:** How should the Keycloak realm be pre-configured? Options: (a) realm export JSON mounted as a volume, (b) Keycloak admin API calls in a startup script. The realm needs at least two clients (webapp, webapi), two roles (admin, customer), and a few test users.

2. **Admin UI scope:** The menu management write path requires admin endpoints. Should this be a separate admin frontend, a section within the existing webapp, or API-only (exercised via curl/Swagger)?

3. **Azure Functions count:** The plan now has 3 functions — `OrderPlacedHandler` (Service Bus), `MenuCatalogSyncHandler` (Service Bus), `AnalyticsProcessor` (Event Hubs). Should these be in one Functions project or split?
