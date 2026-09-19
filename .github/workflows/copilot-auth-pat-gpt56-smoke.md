---
name: Copilot PAT Provider-Qualified Model Smoke Test
description: Verifies provider-qualified model inference using the COPILOT_GITHUB_TOKEN secret.

on:
  pull_request:
    types: [opened, reopened, synchronize]

permissions:
  contents: read
  copilot-requests: none

timeout-minutes: 5
model: copilot/gpt-5.6
---

# Copilot PAT Provider-Qualified Model Smoke Test

Verify that the Copilot engine can complete an inference request using the
`COPILOT_GITHUB_TOKEN` repository secret and the provider-qualified
`copilot/gpt-5.6` model.

Do not inspect the pull request, read repository files, or make changes.
Call `noop` exactly once with the message:

`Copilot provider-qualified inference succeeded with COPILOT_GITHUB_TOKEN.`
