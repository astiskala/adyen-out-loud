# 0009: Pair devices with receipt codes before they can listen

## Status
Accepted. It supersedes the unauthenticated WebSocket in [ADR 0008](0008-single-shared-relay-and-prerecorded-audio.md).

## Context
Anyone who knew a terminal serial (it's printed on the device) could listen to that terminal's payment
metadata. Adding authentication needs something the merchant has and outsiders don't. The Display
webhook carries no per-account secret and can't put a code on the terminal's screen, and asking for
Customer Area access or a per-account secret would bring back the setup that ADR 0008 removed.

## Decision
A device pairs once by quoting the last 4 characters of the PSP reference on **two** receipts from
approved payments on that terminal in the last 15 minutes. The relay remembers those codes from the
webhooks it already receives, issues a random token on a match, and requires the token on
`/ws/<serial>`. Two receipts, because every customer takes one away. Failed attempts are limited per
terminal. Details are in the [threat model](../threat-model.md#pairing).

## Consequences
- A device needs a one-time pairing, but no Customer Area access and nothing typed into Adyen.
- The Durable Object now stores a little state: recent receipt codes (15 minutes), failed-attempt
  counts, and hashes of device tokens.
- Anyone holding two recent receipts from a terminal can still pair. Someone who knows a serial can
  block new pairings for 15 minutes by using up its attempts. There is no per-device revocation.
