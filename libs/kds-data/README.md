# KDS Data

EF Core DbContext, entity definitions, and migrations for the Kitchen Display System read model.

## Scope

Active order queue, station routing, item prep status, and timing metrics. Populated via events from `order-processing-functions`, not by direct database sharing with the ordering domain.

## Used By

- `kds-api` — exclusive owner
