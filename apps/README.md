# Apps

Deployable units for the online ordering platform. Each subdirectory is an independently deployable application.

## Structure

| App                          | Type            | Description                                             |
| ---------------------------- | --------------- | ------------------------------------------------------- |
| `ordering-web`               | Web App         | Customer-facing online ordering website                 |
| `ordering-mobile`            | Mobile App      | Customer-facing native mobile ordering app              |
| `ordering-api`               | API             | Shared backend for `ordering-web` and `ordering-mobile` |
| `admin-web`                  | Web App         | Administrator back-office management UI                 |
| `admin-api`                  | API             | Backend for `admin-web`                                 |
| `kds-web`                    | Web App         | Kitchen Display System for kitchen staff                |
| `kds-api`                    | API             | Backend for `kds-web`                                   |
| `payment-api`                | API             | Payment processing (Stripe)                             |
| `menu-api`                   | API             | Menu data service with caching (REST)                   |
| `order-processing-functions` | Azure Functions | Event-driven order lifecycle state machine              |
| `notification-functions`     | Azure Functions | Event-triggered notifications (email, push, SMS)        |

## Naming Convention

Apps follow a `{domain}-{platform}` pattern:

- **Domain** identifies the business area (`ordering`, `admin`, `kds`, `payment`, `menu`, `notification`, `order-processing`)
- **Platform** identifies the deployment target (`web`, `mobile`, `api`, `functions`)

Frontend apps and their corresponding APIs share the same domain prefix, making the relationship between them clear. Background services use the `-functions` suffix to indicate they are event-driven Azure Functions.
