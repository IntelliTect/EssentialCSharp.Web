---
name: Copilot PAT Concrete Model Smoke Test
description: Verifies concrete-model inference using the COPILOT_GITHUB_TOKEN secret.

on:
  pull_request:
    types: [opened, reopened, synchronize]

permissions:
  contents: read

timeout-minutes: 5
model: gpt-5.6
---

# Copilot PAT Concrete Model Smoke Test

Verify that the Copilot engine can complete an inference request using the
`COPILOT_GITHUB_TOKEN` repository secret and the concrete `gpt-5.6` model.

Do not inspect the pull request, read repository files, or make changes.
Call `noop` exactly once with the message:

`Copilot concrete-model inference succeeded with COPILOT_GITHUB_TOKEN.`
