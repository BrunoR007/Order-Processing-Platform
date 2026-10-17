# 1. RabbitMQ and at-least-once delivery

Status: accepted

## Context

The demo needs durable work queues, consumer acknowledgements, routing, delayed
retries, and inspectable dead letters. It does not require an indefinitely replayable
event log or partition-based stream processing.

## Decision

Use the RabbitMQ .NET client directly. Events are persistent messages routed through
a durable topic exchange to durable quorum queues. Publishers await confirms with
mandatory routing. Consumers acknowledge only after their local transaction commits.

## Consequences

A successful publisher confirm is not proof that a consumer processed an event.
Database commits and broker acknowledgements cannot be made atomic here; duplicate
delivery is expected. A transactional inbox and business keys protect local effects.
Kafka would be a stronger fit for retained streams and independent historical replay,
but would introduce concerns this example does not need to demonstrate.
The single local broker provides persistence, not multi-node high availability.
