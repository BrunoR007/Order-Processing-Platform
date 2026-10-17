# 2. Choreography and explicit compensation

Status: accepted

## Context

Three services participate in an order workflow. Each owns its database; distributed
transactions would introduce coordination and operational coupling.

## Decision

Services react to integration events. Orders maintains a local projection of workflow
progress, but does not issue step-by-step commands. Inventory reserves on OrderCreated.
Payments decides on StockReserved. Inventory releases on PaymentFailed. Orders cancels
only after StockReleased, or directly after StockReservationFailed.

## Consequences

Compensation is asynchronous and can fail independently. The order remains
Compensating until stock release is observed. Technical retry exhaustion leaves a
pending workflow requiring operator intervention, rather than pretending the business
operation failed. Events can arrive out of order across publishers: authoritative
payment/release outcomes can advance Orders before a delayed StockReserved.
More steps, deadlines, refunds, or manual approval would justify an explicit orchestrator.
