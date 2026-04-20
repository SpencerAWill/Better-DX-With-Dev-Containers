# Common

Shared infrastructure library consumed by all APIs, functions, and the store-gateway. Provides cross-cutting concerns so that services are built consistently.

## Contents

- **Authentication middleware** — Keycloak JWT Bearer validation setup, shared across all APIs
- **Client credentials** — Typed HTTP client that acquires, caches, and refreshes service-to-service tokens via Keycloak client credentials flow
- **Correlation ID propagation** — Middleware that reads or generates a correlation ID on inbound requests and forwards it on outbound calls, enabling distributed tracing across services
- **Health checks** — Standardized `/health` (liveness) and `/ready` (readiness) endpoint registration
- **Outbox pattern** — Base implementation for writing events to a local outbox table, used by edge-deployed services for store-and-forward during offline operation

## Used By

All backend services:

- ordering-api, admin-api, kds-api, payment-api, menu-api
- order-processing-functions, notification-functions
- store-gateway
