---
title: Adyen Out Loud — set-up guide
---

# Adyen Out Loud — set-up guide

Adyen Out Loud plays a spoken "Payment successful" on a phone, tablet, or computer next to the till
whenever a terminal payment is approved, in English, Chinese, Malay, or Tamil.

> **Unofficial project.** "Adyen" is a trademark of Adyen N.V. This project is independent and not
> affiliated with, endorsed by, or supported by Adyen.

Setup has two parts. If you use an Adyen partner that has already done part 1 for you, skip to part 2.

## 1. Add the webhook in your Adyen account (once)

Someone with Adyen Customer Area access creates a **Display webhook** (Terminal API notifications)
pointing at:

```text
https://adyenoutloud.adam-eea.workers.dev/webhook
```

Use method `POST` with JSON content, and make sure it is active. It is the same URL for everyone, and
it is the only webhook the app needs. See Adyen's
[webhook documentation](https://docs.adyen.com/development-resources/webhooks/) if you can't find the
setting.

## 2. Set up the app on each device

![The app after setup, showing LISTENING and the latest payment](images/app-listening.png)

Each device is paired with one terminal, once. You need two receipts from that terminal, so you can
do it without Customer Area access.

1. Take two approved payments on the terminal (any amount) and keep both receipts.
2. Within 15 minutes, open the app and enter the terminal's **serial number**: the part of the
   terminal ID after the model prefix (`324688170` from `V400m-324688170`; it is also printed on the
   terminal).
3. Enter the **last 4 characters of the PSP reference** printed on each receipt and tap **Pair**. The
   status changes to **LISTENING**. Each receipt pairs one device; for another device, take two new
   payments.
4. Choose the announcement language and tap **Test voice** to hear it, then take a payment. It
   appears under **Latest event** and the recording plays within a few seconds.

If pairing says the codes don't match, check the serial number and that both payments were approved
in the last 15 minutes. Part 1 must already be done: the relay only sees payments from terminals whose
webhook points at it. If nothing plays, check that the status says **LISTENING**, the webhook is
active, and the volume is up. On iOS the app must stay in the foreground.

## Download

Android (`.apk`) and Windows (installer `.exe` or portable `.zip`) builds are attached to each
[GitHub release](https://github.com/astiskala/adyen-out-loud/releases). On Android, allow installing
apps from your browser or file manager. Windows builds aren't code-signed, so SmartScreen may ask you
to confirm (**More info** > **Run anyway**).

## Good to know

- Only approved payments are announced, always with the same message: Adyen's Display notification
  has no amount or payment method.
- Payments aren't queued. A device that is offline when a payment happens misses it.
- Only paired devices can listen. See the [threat model](threat-model.md) for what pairing does and
  doesn't protect against.
- For account issues use [Adyen's support](https://www.adyen.com/contact), and never paste live payment
  data into a GitHub issue.
