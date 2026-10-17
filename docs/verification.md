# Local verification record

Verified on 2026-10-17 with SDK 10.0.401 and the Docker Linux engine. This is a
functional verification record, not a benchmark or evidence of a remote CI run.

| Check | Observed result |
| --- | --- |
| Locked NuGet restore, with auditing enabled | Passed |
| Release build | Passed with zero warnings and zero errors |
| dotnet format --verify-no-changes | Passed |
| xUnit unit tests | 7 passed |
| TestServer and Testcontainers integration tests | 30 passed |
| Compose startup | All four services and infrastructure started |
| Health endpoints | Eight service endpoints returned healthy |
| Database isolation | Four own-database logins succeeded; twelve cross-service connections were denied |
| Complete workflow | A fictional order reached Confirmed |
| Cross-service trace | Tempo returned spans from orders, inventory, payments, and notifications |
| Reliability metrics | Prometheus returned outbox, publish, process, and dispatch-latency metrics |
| Broker outage | API accepted an order and the pending outbox metric increased while RabbitMQ was stopped |
| Broker recovery | The persisted order reached Confirmed after RabbitMQ restarted |
| Real DLQ CLI | Inspection retained the dead letter; replay after repair confirmed the original order |
| Publication safety | Tracked-file guard and redacted Git-history secret scan passed |

Integration tests also verify cancellation after stock release, absence of overselling
under concurrent requests, duplicate reservation/payment protection, transaction
rollback when outbox persistence fails, and redelivery when publishing succeeds but
dispatch commit fails. Trace propagation is asserted for all four consumers on both
successful and compensating workflows.

Local payment simulation overrides used for the CLI exercise were removed afterward.
No credential values, raw environment files, or machine-specific test output are
included in this record. Test results and temporary Git bundles remain ignored.

The GitHub Actions workflow has not yet run remotely: the repository has no configured
GitHub destination. Container image vulnerability scans, load tests, replicated broker
failure, production deployments, and backup/restore exercises have not been performed.
