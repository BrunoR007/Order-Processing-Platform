# Reproducing failures and recovering messages

Run from the repository root with the Compose stack available. These scenarios use
only fictional orders. They consume the sample stock; run on a disposable local environment.

## Successful order

Set PAYMENT_FAILURE_RATE=0 and PAYMENT_TRANSIENT_FAILURES=0 in .env, then:

```sh
docker compose up -d --force-recreate payments
```

```powershell
./scripts/Demo.ps1 -Expected Confirmed
```

## Payment rejection and compensation

Set PAYMENT_FAILURE_RATE=1 and PAYMENT_TRANSIENT_FAILURES=0, recreate payments as
above, then run Demo.ps1 -Expected Cancelled. Inventory releases the reservation
before Orders becomes Cancelled. No technical retry is necessary for this business outcome.

## Temporary provider outage

Set PAYMENT_FAILURE_RATE=0 and PAYMENT_TRANSIENT_FAILURES=2, recreate payments,
then run Demo.ps1 -Expected Confirmed. The message succeeds after the first two
backoff queues. Retry spans and metrics show the attempts.

## Retry exhaustion and replay

1. Set PAYMENT_FAILURE_RATE=0 and PAYMENT_TRANSIENT_FAILURES=10; recreate payments.
2. Create an order using the README POST example. It remains AwaitingPayment after
   the retries exhaust; Demo.ps1 intentionally times out for this scenario.
3. Inspect the pending message (wait at least 21 seconds for the three TTL stages):

```sh
docker compose run --rm --build deadletter inspect payments 1
```

4. Set PAYMENT_TRANSIENT_FAILURES=0 and recreate payments to repair the cause.
5. Replay and GET the original order until it is Confirmed:

```sh
docker compose run --rm deadletter replay payments 1
```

The CLI forwards only to Payments and keeps the message ID. A second delivery after
commit cannot create a second payment. Malformed envelopes stay on the DLQ when
replay fails validation; do not delete them to make the dashboard look healthy.

## Broker outage and outbox recovery

```sh
docker compose stop rabbitmq
```

POST a new order. PostgreSQL accepts it and retains its outbox message while broker
readiness fails. GET /orders still works. Then:

```sh
docker compose start rabbitmq
```

The relay reconnects and drains pending events. Do not use Compose up during the
outage demonstration: dependency startup checks can intentionally wait for RabbitMQ.

## Troubleshooting

- Use docker compose logs --tail 100 orders inventory payments notifications for errors.
- Check /health/ready and the Grafana outbox backlog before replaying.
- Inventory depletion is a valid cancellation; the sample starts with 50 of each SKU.
- Database initialization and broker default credentials apply only to empty volumes.
  Changing .env does not rotate credentials in existing volumes.
- Preserve data while stopping with docker compose down. For a disposable reset,
  docker compose down --volumes deletes all local business and telemetry volumes.
- Production recovery would require authorization, audit records, backups, and
  reconciliation; this CLI is a local engineering demonstration.
