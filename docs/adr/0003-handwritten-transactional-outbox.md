# 3. A handwritten transactional outbox

Status: accepted

## Context

Saving an order and then publishing directly risks losing the event after a process
crash. Publishing first risks announcing an order whose database transaction fails.

## Decision

EF Core writes business changes and serialized integration events to a local outbox
in the same PostgreSQL transaction. A BackgroundService selects pending rows using
FOR UPDATE SKIP LOCKED, publishes with confirms, and commits dispatch timestamps.
The implementation is in MessageDbContext and OutboxRelay, not an external framework.

## Consequences

A crash after publish but before dispatch commit causes redelivery of the same ID.
A database-trigger integration test exercises that exact window. The relay holds
row locks during network I/O; small batches simplify correctness but cap throughput.
A poison outbox record can block its batch. There is no automatic outbox quarantine,
retention job, or CDC-based relay. Those are explicit next steps, not implied guarantees.
