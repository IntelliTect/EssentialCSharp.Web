---
name: Copilot Inference Smoke Test
description: Verifies organization-billed Copilot inference from a pull request.

on:
  pull_request:
    types: [opened, reopened, synchronize]

permissions:
  contents: read
  copilot-requests: write

timeout-minutes: 5
model: gpt-5.6
---

# Copilot Inference Smoke Test

This workflow only verifies that the Copilot engine can start and complete an
inference request using the GitHub Actions token.

Do not inspect the pull request, read repository files, or make changes.
Call `noop` exactly once with the message:

`Copilot inference succeeded with the GitHub Actions token.`
