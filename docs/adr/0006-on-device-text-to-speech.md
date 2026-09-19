# 0006: On-device text-to-speech, no network translation

## Status
Superseded by [ADR 0008](0008-single-shared-relay-and-prerecorded-audio.md) — announcements are now pre-recorded MP3s.

## Context
Announcements need to be spoken in the language the merchant's staff and customers expect —
English, Chinese, Malay, and Tamil for a Singapore-market retail context — reliably and with
minimal latency, at the moment a payment succeeds. A cloud TTS or translation API would add
network dependency, latency, and cost to every single announcement, and would send payment
metadata (amount, payment method, terminal ID) to a third-party service — a privacy trade-off this
project deliberately avoids (see [`docs/privacy.md`](../privacy.md)).

## Decision
Speech is generated entirely on-device via MAUI's `TextToSpeech` API
(`app/AdyenOutLoud/Services/MauiSpeechService.cs`), using announcement sentences that are
pre-translated and stored as static resources
(`app/AdyenOutLoud.Core/Resources/Strings.{zh,ms,ta}.resx`), not machine-translated at runtime. The
locale/voice **selection** logic (given a requested locale like `zh-SG`, pick the best installed
voice) is a pure, independently-tested function in Core
([`SpeechLocaleSelector`](../../app/AdyenOutLoud.Core/Services/SpeechLocaleSelector.cs)) — the MAUI
service layer only bridges that decision to the platform TTS call.

## Consequences
- An announcement never depends on network connectivity, a translation API being up, or an API key
  — it only depends on the OS's installed voices, which is why the selection algorithm has explicit
  fallback behavior (exact locale → same base language → the platform's first reported voice)
  instead of assuming the exact requested locale/voice is always installed.
- No payment data (amount, payment method, terminal ID) is ever sent to a third-party service to
  produce the spoken sentence.
- The translated sentence templates are fixed, reviewable strings, not runtime-generated text — a
  translation error is a static, auditable diff to a `.resx` file, not a non-deterministic model
  output. The current `zh`/`ms`/`ta` translations were written for this project and should be
  reviewed by a native speaker before this project is used in a live retail deployment; see the
  note in [`docs/testing.md`](../testing.md#localization).
- Voice quality and language coverage are bounded by what each OS/device actually ships — a very
  old or minimally-configured device may not have a Tamil or Malay voice installed at all, in which
  case the fallback logic picks the platform's default rather than failing the announcement.
