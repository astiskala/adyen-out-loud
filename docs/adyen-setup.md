# Adyen setup

This project listens for one Adyen webhook type — the terminal's own **Display** webhook — and
routes it to the right device using the terminal serial number configured in the app. See
[`docs/architecture.md`](architecture.md) for why Display alone is enough (and what it means for
announcements: always a generic "payment successful," never an amount or card scheme).

## 1. Generate a company token (needs Adyen Customer Area access)

Whoever manages the company's Adyen account generates one token, once, for the whole company:

```bash
scripts/generate-company-token.sh
```

This prints a 43-character token. The company's relay URL is:

```text
https://<your-worker-host>/v1/c/<company-token>
```

**Treat this URL as a secret** — see [`docs/threat-model.md`](threat-model.md). Anyone who has it
can send fabricated payment announcements to every terminal in the company.

## 2. Configure the Display webhook (once, for the whole company)

In the Adyen Customer Area: under your terminal/POS integration settings, create a **Display
webhook** (terminal API notifications) and set its server URL to the URL from step 1. Method:
`POST`. This is the only webhook this project needs — there is no Standard notification webhook to
configure.

## 3. Configure the app on each terminal (no Customer Area access needed)

Share the relay URL from step 1 with whoever installs the app on each terminal. In the app:

1. Paste the relay URL into **Relay URL**.
2. Enter this specific device's **terminal serial number** — the part of Adyen's terminal ID after
   the model prefix (e.g. `324688170` from `V400m-324688170`; check the terminal's own label or
   settings screen if unsure).
3. Tap **Save**.

## 4. Verify

1. In the app, tap **Test voice** — confirms text-to-speech works and which installed voice was
   selected for your chosen language (see [`docs/architecture.md`](architecture.md#client-architecture-maui)).
2. Run a real or test transaction on the terminal.
3. The app should show the event under "Latest event" and speak "Payment successful" within a few
   seconds. If nothing happens, check: the relay URL and terminal serial number are entered
   correctly (an exact match to what Adyen sends as `POIID`'s suffix); the Display webhook is
   configured and reaching the Worker (check the Adyen Customer Area's webhook delivery log); the
   app shows "LISTENING" rather than "NEEDS ATTENTION."

## Notes

- **This project is not Adyen Support.** For account-level or merchant-specific issues, use
  [Adyen's own support channels](https://www.adyen.com/contact) — see [`SUPPORT.md`](../SUPPORT.md).
- Do not paste your relay URL or any live payment data into a GitHub issue — see the warnings in
  the [bug report template](../.github/ISSUE_TEMPLATE/bug_report.yml).
- HMAC signature verification is **not** implemented in this project's v1 — see
  [`docs/threat-model.md`](threat-model.md) for what that means and why.
