# Payment API

ASP.NET Core API for payment processing.

## Purpose

Handles payment operations via Stripe — creating payment intents, processing webhooks, checking payment status, and managing refunds. Owns all payment data and enforces idempotency and retry logic.

Isolated from the ordering API to limit PCI compliance surface area and prevent payment failures from cascading into the order flow.
