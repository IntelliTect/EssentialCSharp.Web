---
description: |
  This workflow is an automated accessibility compliance checker for web applications.
  Reviews websites against WCAG 2.2 guidelines using Playwright browser automation.
  Identifies accessibility issues and creates GitHub discussions or issues with detailed
  findings and remediation recommendations. Helps maintain accessibility standards
  continuously throughout the development cycle.

on:
  schedule: weekly
  workflow_dispatch:

permissions:
  actions: read
  contents: read
  copilot-requests: write
  discussions: read
  issues: read
  pull-requests: read
  security-events: read

network: defaults

safe-outputs:
  mentions: false
  allowed-github-references: []
  create-discussion:
    title-prefix: "[accessibility-review] "
    category: "q-a"
    max: 5
  add-comment:
    max: 5

tools:
  playwright:
  web-fetch:
  github:
    toolsets: [all]

timeout-minutes: 15

steps:
  - name: Checkout repository
    uses: actions/checkout@v7.0.1
    with:
      fetch-depth: 0
      persist-credentials: false
  - name: Set up .NET
    uses: actions/setup-dotnet@v6.0.0
    with:
      global-json-file: global.json
  - name: Set up Node.js
    uses: actions/setup-node@v7.0.0
    with:
      node-version-file: EssentialCSharp.Web/.nvmrc
  - name: Build and run app in background
    run: |
      dotnet restore EssentialCSharp.Web.slnx -p:AccessToNugetFeed=false
      npm ci --prefix EssentialCSharp.Web
      npm run build --prefix EssentialCSharp.Web
      dotnet build EssentialCSharp.Web.slnx --configuration Release --no-restore -p:AccessToNugetFeed=false -p:SkipFrontendBuild=true
      ASPNETCORE_URLS=http://127.0.0.1:3000 dotnet run --project EssentialCSharp.Web/EssentialCSharp.Web.csproj --configuration Release --no-build --no-launch-profile > /tmp/essentialcsharp-web.log 2>&1 &
source: githubnext/agentics/workflows/accessibility-review.md@5d11aa2a05ce2c943c085acb7b12b583f83ed375
# Use a concrete model because the auto alias is unavailable to this organization.
model: gpt-5.6
---

# Accessibility Review

Your name is Accessibility Review.  Your job is to review a website for accessibility best
practices.  If you discover any accessibility problems, you should file GitHub issue(s) 
with details.

Our team uses the Web Content Accessibility Guidelines (WCAG) 2.2.  You may 
refer to these as necessary by browsing to https://www.w3.org/TR/WCAG22/ using
the WebFetch tool.  You may also search the internet using WebSearch if you need
additional information about WCAG 2.2.

The code of the application has been checked out to the current working directory.

Steps:

1. Use the Playwright MCP tool to browse to `localhost:3000`. Review the website for accessibility problems by navigating around, clicking
  links, pressing keys, taking snapshots and/or screenshots to review, etc. using the appropriate Playwright MCP commands.

2. Review the source code of the application to look for accessibility issues in the code.  Use the Grep, LS, Read, etc. tools.

3. Use the GitHub MCP tool to create discussions for any accessibility problems you find.  Each discussion should include:
   - A clear description of the problem
   - References to the appropriate section(s) of WCAG 2.2 that are violated
   - Any relevant code snippets that illustrate the issue