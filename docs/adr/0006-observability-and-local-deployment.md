# 6. OpenTelemetry and a reproducible local deployment

Status: accepted

## Context

Recruiters should be able to follow a request across asynchronous service boundaries
and reproduce failure scenarios with one local environment.

## Decision

Use W3C traceparent/tracestate plus correlation and causation IDs in each event envelope.
Producer and consumer spans use the propagated context. ASP.NET Core requests,
HttpClient calls, and reliability metrics are exported through OTLP to the collector.
Tempo stores traces, Prometheus stores metrics, and Grafana provisions both data
sources and a reliability dashboard. No message IDs are metric labels.

Compose runs four service hosts, one RabbitMQ node, and one PostgreSQL instance with
four separately owned databases. Published ports bind to loopback only. Credentials
come from an ignored .env; a sample file contains placeholders only.

## Consequences

One PostgreSQL container shares failure and capacity across databases. Local plaintext
connections and administrative dashboards are unsuitable for a public deployment.
Tempo uses temporary local storage; traces disappear when its container is recreated.
Telemetry is best effort and is not part of business transaction durability. Always-on
trace sampling suits a small demo; production would need sizing, sampling, retention,
TLS, access control, and immutable image digests.
