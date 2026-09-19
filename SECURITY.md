# Security Policy

## Reporting a vulnerability

Please report security vulnerabilities privately using
[GitHub Private Vulnerability Reporting](../../security/advisories/new) on this repository
(**Security** tab → **Report a vulnerability**), not a public issue.

Include, if you can:

- What you found and why it's a vulnerability (not just "this seems wrong")
- Steps to reproduce, or a proof of concept
- The affected version/commit
- Your assessment of impact

We'll acknowledge your report, investigate, and keep you updated as we work on a fix. Please give
us a reasonable amount of time to address the issue before any public disclosure.

## Scope

This applies to the code in this repository: the `worker/` Cloudflare Worker and the `app/` .NET
MAUI client. It does **not** cover Adyen's own platform or terminal firmware — report those to
Adyen directly.

## Supported versions

This project does not yet have tagged releases; treat the `main` branch as the only supported
version until tagged releases exist.

## Known design trade-offs

Before reporting that the relay has no authentication or HMAC verification: this is a known, deliberate
design choice, not an oversight. Read [`docs/threat-model.md`](docs/threat-model.md) first; if
your report is about something beyond what that document already covers, please do still report
it.

## Learn more

[`docs/threat-model.md`](docs/threat-model.md) covers the assets, trust boundaries, and known risks.
