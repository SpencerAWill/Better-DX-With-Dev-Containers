# Contracts

Shared message and event definitions consumed by all services that communicate via Service Bus, Event Hubs, or cross-service HTTP calls.

## Purpose

Defines the agreed-upon schemas for inter-service communication. Any service that publishes or consumes an event, or calls another service's API, references this library to ensure schema consistency.

## Contents

- **Events** — Event Hub broadcast schemas (e.g., OrderConfirmedEvent, StatusChangedEvent, MenuUpdatedEvent)
- **Commands** — Service Bus command schemas (e.g., ProcessOrderCommand, RebuildMenuCatalogCommand)
- **DTOs** — Shared request/response types for synchronous API-to-API calls (e.g., CreatePaymentIntentRequest)

## Used By

All services that participate in inter-service communication:

- ordering-api, payment-api, menu-api, admin-api, kds-api
- order-processing-functions, notification-functions
- store-gateway
