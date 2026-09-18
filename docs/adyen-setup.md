# Adyen setup

This project listens for two independent Adyen webhook types and correlates them — see
[`docs/architecture.md`](architecture.md) for why both are needed. You configure **the same URL**
for both.

## 1. Get the webhook URL from the app

Launch the app. On first run it generates an instance token and shows the webhook URL as a
read-only, copyable field:

```text
https://<your-worker-host>/v1/i/<43-character-token>
```

**Treat this URL as a secret** — see [`docs/threat-model.md`](threat-model.md). Anyone who has it
can send this instance fabricated payment announcements.

## 2. Configure the Display webhook (from the terminal)

In the Adyen Customer Area: **Developers → Webhooks → Create new webhook → Standard notification**
is *not* this one — you specifically need a **Display webhook** (terminal API notifications), under
your terminal/POS integration settings. Set its server URL to the URL from step 1. Method: `POST`.

## 3. Configure the Standard webhook (from the platform)

**Developers → Webhooks → Create new webhook → Standard notification webhook.** Set its server URL
to the **exact same URL** from step 1. Method: `POST`. At minimum, enable the `AUTHORISATION`
event.

## 4. Verify

1. In the app, tap **Test voice** — confirms text-to-speech works and which installed voice was
   selected for your chosen language (see [`docs/architecture.md`](architecture.md#client-architecture-maui)).
2. Run a real or test transaction on the terminal.
3. The app should show the event under "Latest event" and speak the announcement within a few
   seconds. If only the terminal-side (generic, no amount/method) version is announced, the
   Standard webhook likely isn't configured, isn't reaching the Worker, or is pointed at a
   different URL — check the Adyen Customer Area's webhook delivery log for that specific webhook.

## Notes

- **This project is not Adyen Support.** For account-level or merchant-specific issues, use
  [Adyen's own support channels](https://www.adyen.com/contact) — see [`SUPPORT.md`](../SUPPORT.md).
- Do not paste your webhook URL, instance token, or any live payment data into a GitHub issue —
  see the warnings in the [bug report template](../.github/ISSUE_TEMPLATE/bug_report.yml).
- HMAC signature verification is **not** implemented in this project's v1 — see
  [`docs/threat-model.md`](threat-model.md) for what that means and why.
