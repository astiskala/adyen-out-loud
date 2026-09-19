# 0008: One shared relay URL and pre-recorded announcements

## Status
Accepted. Supersedes the company-token routing in
[ADR 0007](0007-display-only-stateless-company-scoped-relay.md) and the on-device speech synthesis in
[ADR 0006](0006-on-device-text-to-speech.md).

## Context
Per-company relay URLs meant someone had to generate a secret token, configure it in Adyen, and hand
the same URL to every installer, who then had to paste it into the app. Since the relay stores
nothing and only forwards a notification to whichever app is connected for that terminal, the
per-company secret protected very little and cost a lot of setup friction. Separately, platform
text-to-speech quality and voice availability varied for Chinese, Malay, and Tamil.

## Decision
1. **One relay for everyone:** `https://adyenoutloud.adam-eea.workers.dev`. Adyen's Display webhook is pointed at
   `https://adyenoutloud.adam-eea.workers.dev/webhook`; apps connect to `wss://…/ws/<terminalSerial>` and the URL is compiled into the
   app (Debug builds may override it with the `ADYEN_OUT_LOUD_RELAY_URL` environment variable for
   testing). A single Durable Object holds every socket, tagged by terminal serial; a notification
   whose terminal has no connected socket is dropped.
2. **Pre-recorded audio:** the app bundles `PaymentReceived-{EN,ZH,MS,TA}.mp3` and
   `TestAnnouncement-{EN,ZH,MS,TA}.mp3` (`app/AdyenOutLoud/Resources/Raw`) and plays them with
   `Plugin.Maui.Audio`. There is no text-to-speech, no `.resx` localization, and no voice selection.

## Consequences
- No token generation, no relay URL in the app, no "keep this URL secret" warning.
- The terminal serial is the only routing key and it is not secret: anyone who knows it can spoof or
  listen to that terminal's announcements. Accepted; see [`docs/threat-model.md`](../threat-model.md).
  HMAC verification of Adyen's webhook remains the future hardening path.
- One Durable Object serves all traffic, so throughput is bounded by a single object; shard by
  terminal serial if that ever matters.
- Announcement wording and voice are whatever is in the MP3 files; changing them means replacing the
  files and shipping a new build.
