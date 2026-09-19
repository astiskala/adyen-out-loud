# Adyen Out Loud

[![Quality](https://github.com/astiskala/adyen-out-loud/actions/workflows/quality.yml/badge.svg)](https://github.com/astiskala/adyen-out-loud/actions/workflows/quality.yml)
[![CodeQL](https://github.com/astiskala/adyen-out-loud/actions/workflows/codeql.yml/badge.svg)](https://github.com/astiskala/adyen-out-loud/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Play a spoken "Payment successful" on a phone, tablet, or PC next to the till whenever an Adyen
terminal payment is approved, in English, Chinese, Malay, or Tamil.

![The app showing LISTENING and the latest payment](docs/images/app-listening.png)

> **Unofficial project.** "Adyen" is a trademark of Adyen N.V. This project is independent and is not
> affiliated with, sponsored by, or endorsed by Adyen. The name is used only to describe
> interoperability with Adyen's terminal and webhook APIs.

## How it works

```text
Adyen terminal ─▶ Cloudflare Worker ─▶ Durable Object ─▶ WebSocket ─▶ app ─▶ bundled recording
```

Adyen's Display webhook goes to one shared relay, which forwards each approved payment to the app
paired with that terminal and drops it if none is connected. The app runs on Android, iOS, macOS, and
Windows (one .NET MAUI codebase).

## Set up

Add the webhook in your Adyen account once (`https://adyenoutloud.adam-eea.workers.dev/webhook`), then
pair each device with a terminal: its serial number plus the last 4 characters of the PSP reference
on two recent receipts. Step by step: the [set-up guide](https://astiskala.github.io/adyen-out-loud/).
Android and Windows builds are on the [releases page](https://github.com/astiskala/adyen-out-loud/releases).

## Limitations

- **Pairing proves access to receipts, not identity.** Anyone holding two recent receipts from a
  terminal can pair with it. Webhooks are only accepted from Adyen's IP addresses. Read the
  [threat model](docs/threat-model.md) before relying on it.
- The announcement is always the same generic message: Adyen's Display notification has no amount or
  payment method.
- Devices that are offline miss payments, and iOS can only listen in the foreground.

## Development

See [`docs/development.md`](docs/development.md) for setup, tests, CI/CD, and deployment,
[`docs/architecture.md`](docs/architecture.md) for how it fits together, and
[`CONTRIBUTING.md`](CONTRIBUTING.md) (or [`AGENTS.md`](AGENTS.md) for coding agents) before sending a
change. Report vulnerabilities privately per [`SECURITY.md`](SECURITY.md).

## License

[MIT](LICENSE)
