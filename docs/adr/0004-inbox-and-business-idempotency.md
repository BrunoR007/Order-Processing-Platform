# 4. Inbox deduplication and business idempotency

Status: accepted

## Context

Messages may be redelivered, and callers may repeat requests after a timeout.
Different message IDs can also represent the same business operation.

## Decision

A consumer inserts (consumer, message_id) with ON CONFLICT DO NOTHING inside the
same transaction as its business changes and outgoing events. PostgreSQL's unique
constraint serializes concurrent duplicate deliveries. Failed transactions roll back
the inbox claim. Reservations, payments, and notifications also use order ID as a
business key. Conditional stock updates prevent negative inventory under concurrency.

POST /orders requires an Idempotency-Key. Canonical SKU quantities are hashed. An
identical retry returns the original order; changed content returns 409. Prices come
from the fictional server-side catalog and are snapshotted with the order.

## Consequences

These guarantees cover local database effects. A real payment provider would need
its own idempotency key and reconciliation. Logging a simulated notification before
commit can duplicate the log on crash, although the notification database row remains
unique. Inbox retention must exceed the supported replay horizon; cleanup is not yet implemented.
