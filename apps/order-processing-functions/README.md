# Order Processing Functions

Azure Functions for event-driven order lifecycle management.

## Purpose

Implements the order state machine (placed → confirmed → preparing → ready → completed). Subscribes to events from Service Bus/Event Hubs and produces events that other services react to (e.g., notifications, KDS updates).

No HTTP endpoints — purely event-driven.
