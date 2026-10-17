# 5. Bounded retries and dead-letter replay

Status: accepted

## Context

A business rejection is a valid outcome. Temporary infrastructure failures require
retries; retrying a poison message indefinitely wastes resources and hides incidents.

## Decision

Technical failures forward to per-consumer TTL queues with delays of 1, 5, and 15
seconds. These quorum queues use at-least-once dead lettering and reject-publish
overflow. Application forwarding awaits publisher confirms before acknowledging the
original. After three retries, forwarding goes to the service's dead-letter queue.
Malformed JSON goes directly there. Main queues also dead-letter after their broker
delivery limit, protecting against repeated disconnect/redelivery cycles.

The CLI inspects without acknowledging, or replays a bounded number directly to the
affected consumer. Replay keeps the original message/correlation/causation IDs and
trace context while resetting the attempt counter. It does not rebroadcast to peers.

## Consequences

Forward-then-ack can duplicate messages; the inbox handles them. Inspection can change
queue ordering because it requeues deliveries. Replay needs an operator to fix the
underlying cause first. There is no automated repair of malformed envelopes, jitter,
circuit breaker, or production RBAC. Queue arguments are versioned in code; changing
immutable arguments requires a deliberate topology migration.
