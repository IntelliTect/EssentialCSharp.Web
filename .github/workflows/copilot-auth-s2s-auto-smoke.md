---
name: Copilot Actions Token Auto Smoke Test
description: Verifies auto-model startup and inference using the GitHub Actions token.

on:
  pull_request:
    types: [opened, reopened, synchronize]

permissions:
  contents: read
  copilot-requests: write

timeout-minutes: 5
model: auto
---

# Copilot Actions Token Auto Smoke Test

Verify that the Copilot engine can resolve `auto` and complete an inference
request using the GitHub Actions token.

Do not inspect the pull request, read repository files, or make changes.
Call `noop` exactly once with the message:

`Copilot auto inference succeeded with the GitHub Actions token.`
