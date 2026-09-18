# 0005: Correlate Display and Standard webhooks instead of trusting one

## Status
Accepted

## Context
Adyen terminals send a Display notification (`TENDER_FINAL`) that the terminal itself considers
authoritative about approval, but it never carries payment method or amount. Adyen's platform
separately sends a Standard `AUTHORISATION` webhook that carries both, but can arrive before,
after, or (on decline, or a delivery failure) never, relative to the Display notification. Relying
on only one of them means either never announcing payment method/amount, or occasionally never
announcing a genuinely successful payment because its Authorisation webhook was lost.

## Decision
Store both notification types independently, keyed by `pspReference`, and correlate them in
[`payment-correlator.ts`](../../worker/src/adyen/payment-correlator.ts): publish only when the
Display notification is successful; if a successful Authorisation is already known, publish a rich
announcement (with method and amount); otherwise wait `CORRELATION_WAIT_MS` (5 seconds) for one to
arrive, and if it doesn't, publish a generic announcement (method and amount both `null`). Publish
at most once per `pspReference`, ever — a late Authorisation can never upgrade or duplicate an
already-published announcement. See
[`docs/architecture.md`](../architecture.md#durable-object-workersrcrelay-objectts) for the full
ingest → correlate → publish sequence and its concurrency tests.

## Consequences
- A successful payment is always announced, even if its Authorisation webhook is delayed
  indefinitely or never arrives — the generic fallback guarantees this.
- A declined or otherwise unsuccessful Display notification is never announced, regardless of what
  the Authorisation webhook says — the Display notification is the terminal's own ground truth
  about what happened at the point of sale.
- The 5-second correlation wait is a real, tunable trade-off between "wait longer for a richer
  announcement" and "announce sooner." It is short enough that a customer standing at the terminal
  doesn't notice the delay in the common case where both webhooks arrive close together.
- This requires the Worker to persist state per `pspReference` and run an alarm-driven fallback
  rather than being a stateless pass-through — see [ADR 0002](0002-use-cloudflare-durable-objects.md).
- Arrival-order independence, duplicate-delivery idempotency, and the at-most-one-publication
  guarantee are all covered by concurrency tests in `worker/test/worker.test.ts` (see
  [`docs/testing.md`](../testing.md)), because this correlation logic is exactly the kind of code
  where an ordering bug would silently double-announce or silently drop a payment.
