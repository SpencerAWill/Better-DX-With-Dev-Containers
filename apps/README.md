# Apps

Deployable units for the online ordering platform. Each subdirectory is an independently deployable application.

## Structure

| App                          | Type            | Deploy Zone  | Description                                                 |
| ---------------------------- | --------------- | ------------ | ----------------------------------------------------------- |
| `ordering-web`               | Web App         | Cloud        | Customer-facing online ordering website                     |
| `ordering-mobile`            | Mobile App      | Cloud        | Customer-facing native mobile ordering app                  |
| `ordering-api`               | API             | Cloud + Edge | Shared backend for `ordering-web` and `ordering-mobile`     |
| `admin-web`                  | Web App         | Cloud        | Administrator back-office management UI                     |
| `admin-api`                  | API             | Cloud        | Backend for `admin-web`                                     |
| `kds-web`                    | Web App         | Edge         | Kitchen Display System for kitchen staff                    |
| `kds-api`                    | API             | Edge         | Backend for `kds-web`                                       |
| `payment-api`                | API             | Cloud + Edge | Payment processing (Stripe, store-and-forward at edge)      |
| `menu-api`                   | API             | Cloud + Edge | Menu data service with caching (REST, read replica at edge) |
| `order-processing-functions` | Azure Functions | Cloud + Edge | Event-driven order lifecycle state machine                  |
| `notification-functions`     | Azure Functions | Cloud        | Event-triggered notifications (email, push, SMS)            |
| `store-gateway`              | Worker Service  | Edge         | Sync agent, outbox forwarding, connectivity monitoring      |

## Deployment Zones

- **Cloud** — Hosted in the cloud. Serves online customers and administrators.
- **Edge** — Runs on-prem at each store location. Must operate independently during network outages.
- **Cloud + Edge** — Deployed in both zones. Edge instances handle local operations offline; cloud instances handle online traffic. The `store-gateway` synchronizes data between zones.

## Naming Convention

Apps follow a `{domain}-{platform}` pattern:

- **Domain** identifies the business area (`ordering`, `admin`, `kds`, `payment`, `menu`, `notification`, `order-processing`, `store`)
- **Platform** identifies the deployment target (`web`, `mobile`, `api`, `functions`, `gateway`)

Frontend apps and their corresponding APIs share the same domain prefix, making the relationship between them clear. Background services use the `-functions` suffix to indicate they are event-driven Azure Functions.
