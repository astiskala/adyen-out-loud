---
title: Adyen Out Loud — set-up guide
---

# Adyen Out Loud — set-up guide

Adyen Out Loud plays a spoken "Payment successful" on a phone, tablet, or computer next to the till
whenever a terminal payment is approved — in English, Chinese, Malay, or Tamil. Setting it up takes
two minutes and has two parts: someone with **Adyen Customer Area** access adds one webhook (once,
for the whole account), and each device's app is told which terminal it belongs to.

> **Unofficial project.** "Adyen" is a trademark of Adyen N.V. This project is independent and not
> affiliated with, endorsed by, or supported by Adyen.

## 1. Add the webhook in the Adyen Customer Area (once per account)

The same URL is used by everyone — there is nothing to generate and no account to create:

```text
https://adyenoutloud.adam-eea.workers.dev/webhook
```

1. Sign in to the [Adyen Customer Area](https://ca-live.adyen.com/) with a user that can manage
   webhooks.
2. In your terminal / POS integration settings, create a **Display webhook** (Terminal API
   notifications). This is the only webhook the app needs — there is no Standard notification
   webhook to configure.
3. Set the **URL** to the address above, with method `POST` and JSON content.
4. Save and make sure the webhook is **active**.

(Adyen's menu names change from time to time; if you can't find the page, see Adyen's
[webhook documentation](https://docs.adyen.com/development-resources/webhooks/).)

## 2. Point each device at its terminal

You do **not** need Customer Area access for this step, and there is no URL to enter.

1. Install and open Adyen Out Loud on the device that sits next to the terminal.
2. Enter the terminal's **serial number** — the part of Adyen's terminal ID after the model prefix
   (for example `324688170` from `V400m-324688170`; it is also printed on the terminal's label).
3. Tap **Save**. The status should change to **LISTENING**.
4. Pick the announcement language.

## 3. Check that it works

1. Tap **Test voice** — you should hear the test announcement in the selected language.
2. Run a real or test payment on the terminal. Within a few seconds the app should show the event
   under **Latest event** and play the "Payment successful" recording.

If nothing plays, check that the app says **LISTENING** (not **NEEDS ATTENTION**), that the serial
number matches exactly what Adyen sends as the suffix of the terminal ID, that the webhook is active,
and that the device volume is up. Keep the app open on iOS — it can only listen in the foreground
there.

## Good to know

- **Only approved payments are announced**, and always with the same generic message — Adyen's
  Display notification carries no amount or payment method.
- **Nothing is stored.** The relay forwards each notification to the app that is currently
  connected for that terminal and drops it if there is none; a device that was offline misses that
  payment.
- **The terminal serial number is the only thing that routes a message**, and it is not a secret.
  Anyone who knows a serial number could send a fake notification for it, or listen to that
  terminal's announcements. Do not treat an announcement as proof of payment; check the terminal or
  your Adyen reports. See the [threat model](threat-model.md) for details.
- **Not Adyen Support.** For account issues use [Adyen's own support](https://www.adyen.com/contact).
  Please don't paste live payment data into a GitHub issue.
