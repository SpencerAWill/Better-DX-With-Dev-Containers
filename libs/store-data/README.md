# Store Data

EF Core DbContext, entity definitions, and migrations for the store-gateway operational data.

## Scope

Outbox tables, sync cursors/checkpoints, connectivity state, and conflict resolution tracking. This is cross-cutting operational data that spans all business domains — it does not belong in any single domain database.

## Key Entities

- **OutboxEvent** — Events produced by edge services awaiting forwarding to cloud messaging (Service Bus / Event Hubs)
- **SyncCheckpoint** — Tracks sync progress per entity type (last synced timestamp, sequence number)
- **ConnectivityLog** — Records connectivity state transitions (online/offline) with timestamps

## Used By

- `store-gateway` — exclusive owner

## Deployment

Edge only. Each store has its own `store_db` instance. This database does not exist in the cloud.
