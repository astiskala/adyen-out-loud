# Architecture Decision Records

An ADR captures a decision that would otherwise get re-litigated, or whose reasoning would
otherwise be lost. We write one when a choice has real trade-offs and long-lived consequences —
not for routine implementation details that are obvious from the code and tests.

## Format

```text
# NNNN-short-title.md

## Status
Proposed | Accepted | Superseded by NNNN | Deprecated

## Context
What situation made a decision necessary. Constraints, not opinions.

## Decision
What we decided, stated plainly.

## Consequences
What this makes easier, what it makes harder, and what it deliberately gives up.
```

## Adding a new ADR

Copy the highest-numbered file, increment the number, fill it in, and link it from
[`docs/architecture.md`](../architecture.md) or [`docs/quality.md`](../quality.md) wherever the
decision is relevant. Superseding an old decision means adding a new ADR and updating the old
one's `Status` line — never silently deleting or rewriting history.

## Index

| ADR | Decision |
| --- | --- |
| [0001](0001-use-dotnet-maui.md) | Use .NET MAUI for the client |
| [0002](0002-use-cloudflare-durable-objects.md) | Use Cloudflare Durable Objects for per-instance state |
| [0006](0006-on-device-text-to-speech.md) | On-device text-to-speech (superseded by 0008) |
| [0007](0007-display-only-stateless-company-scoped-relay.md) | Display-only, stateless, company-scoped relay (routing superseded by 0008) |
| [0008](0008-single-shared-relay-and-prerecorded-audio.md) | One shared relay URL and pre-recorded announcements |
