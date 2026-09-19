# 0008: One shared relay URL and pre-recorded announcements

## Status
Accepted. It replaced per-company relay URLs with secret tokens and on-device text-to-speech.

## Context
Per-company URLs meant generating a secret, configuring it in Adyen, and handing it to every installer
to paste into the app. The relay stores nothing and only forwards to whichever app is connected, so
the secret protected very little. Platform text-to-speech was also inconsistent for Chinese, Malay, and
Tamil.

## Decision
1. **One relay for everyone:** `https://adyenoutloud.adam-eea.workers.dev`. Adyen posts to `/webhook`;
   apps connect to `/ws/<terminalSerial>`. The URL is compiled into the app. A notification for a
   terminal with no connected app is dropped.
2. **Pre-recorded audio:** the app bundles `PaymentReceived-*.mp3` and `TestAnnouncement-*.mp3` for
   EN, ZH, MS, and TA (`app/AdyenOutLoud/Resources/Raw`) and plays them with `Plugin.Maui.Audio`.

## Consequences
- No token to generate, no URL to enter, nothing secret to protect.
- The terminal serial is the only routing key and isn't secret, so anyone who knows it can listen
  to that terminal ([threat model](../threat-model.md)). Webhooks are accepted only from Adyen's IP
  addresses, so announcements can't be forged directly.
- Changing the wording or voice means replacing the MP3s and shipping a new build.
