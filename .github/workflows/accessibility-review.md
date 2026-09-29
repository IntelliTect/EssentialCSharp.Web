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
  - name: Build and run app in background
    run: |
      sql_container=essentialcsharp-web-accessibility-db
      sql_password="Ecs$(uuidgen | tr -d '-')!a1"
      docker rm -f "$sql_container" >/dev/null 2>&1 || true
      docker run -d --name "$sql_container" -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$sql_password" -p 14333:1433 mcr.microsoft.com/mssql/server:2022-latest
      for attempt in $(seq 1 60); do
        if docker exec "$sql_container" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$sql_password" -Q "SELECT 1" >/dev/null 2>&1; then
          break
        fi
        if [ "$attempt" -eq 60 ]; then
          docker logs "$sql_container"
          exit 1
        fi
        sleep 2
      done
      dotnet restore EssentialCSharp.Web.slnx -p:AccessToNugetFeed=false
      npm ci --prefix EssentialCSharp.Web
      npm run build --prefix EssentialCSharp.Web
      dotnet build EssentialCSharp.Web.slnx --configuration Release --no-restore -p:AccessToNugetFeed=false -p:SkipFrontendBuild=true
      connection_string_prefix='Server=127.0.0.1,14333;Database=EssentialCSharp.Web;User ID=sa;Password='
      connection_string_suffix=';TrustServerCertificate=true;Encrypt=false;MultipleActiveResultSets=true;'
      ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:3000 ConnectionStrings__EssentialCSharpWebContextConnection="${connection_string_prefix}${sql_password}${connection_string_suffix}" dotnet run --project EssentialCSharp.Web/EssentialCSharp.Web.csproj --configuration Release --no-build --no-launch-profile > /tmp/essentialcsharp-web.log 2>&1 &
source: githubnext/agentics/workflows/accessibility-review.md@4bc8419fad05e6b032741cbfd189986700bcf71c
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

0. Use the Playwright MCP tool to browse to `localhost:3000`. Review the website for accessibility problems by navigating around, clicking
  links, pressing keys, taking snapshots and/or screenshots to review, etc. using the appropriate Playwright MCP commands.

1. Review the source code of the application to look for accessibility issues in the code.  Use the Grep, LS, Read, etc. tools.

2. Use the GitHub MCP tool to create discussions for any accessibility problems you find.  Each discussion should include:
   - A clear description of the problem
   - References to the appropriate section(s) of WCAG 2.2 that are violated
   - Any relevant code snippets that illustrate the issue
