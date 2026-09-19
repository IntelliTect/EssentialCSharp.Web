---
name: Copilot PAT Auto Smoke Test
description: Verifies auto-model startup and inference using the COPILOT_GITHUB_TOKEN secret.

on:
  pull_request:
    types: [opened, reopened, synchronize]

permissions:
  contents: read
  copilot-requests: none

timeout-minutes: 5
model: auto
---

# Copilot PAT Auto Smoke Test

Verify that the Copilot engine can resolve `auto` and complete an inference
request using the `COPILOT_GITHUB_TOKEN` repository secret.

Do not inspect the pull request, read repository files, or make changes.
Call `noop` exactly once with the message:

`Copilot auto inference succeeded with COPILOT_GITHUB_TOKEN.`
