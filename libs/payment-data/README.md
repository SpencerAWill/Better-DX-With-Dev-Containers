# Payment Data

EF Core DbContext, entity definitions, and migrations for the payment domain.

## Scope

Payment intents, transactions, refund records, and payment status. Isolated for PCI compliance — no other service accesses this database directly.

## Used By

- `payment-api` — exclusive owner
