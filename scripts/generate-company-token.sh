#!/usr/bin/env bash
# Prints a fresh 256-bit company token (43-char base64url) suitable for a relay URL:
# https://<your-worker-host>/v1/c/<token>
# There's no registration step — any well-formed token routes to its own isolated Durable Object
# on first use. Keep the resulting URL secret; see docs/threat-model.md.
set -euo pipefail
openssl rand -base64 32 | tr '+/' '-_' | tr -d '=\n'
echo
