# Getting Started with EssentialCSharp.Web Development

This guide will help you set up your local development environment for working on the Essential C# Web project.

## Prerequisites

- [Visual Studio](https://visualstudio.microsoft.com/) (or your preferred IDE)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) — verify with `dotnet --info`

## Minimal Local Setup

For basic browsing and UI development, no secrets are needed. The database connection and HCaptcha test keys are already configured in `appsettings.Development.json`.

1. Clone the repository.
2. If you have access to the private NuGet feed, set `<AccessToNugetFeed>true</AccessToNugetFeed>` in [Directory.Packages.props](../Directory.Packages.props).
3. Run the project.

> **Tip:** Use the [dotnet secret manager](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets#set-a-secret) for any secrets below:
> `dotnet user-secrets set "<Key>" "<Value>" --project EssentialCSharp.Web`

## Optional Features

These features are disabled or use safe defaults in Development unless explicitly configured.

### AI Chat

Required to enable the chat widget. Skipped entirely in Development if not configured.

```
AIOptions:Endpoint = https://<your-azure-openai-resource>.openai.azure.com/
AIOptions:VectorGenerationDeploymentName = text-embedding-3-large-v1
AIOptions:ChatDeploymentName = gpt-4o
ConnectionStrings:PostgresVectorStore = <postgres-connection-string>
```

### MCP Server

The MCP endpoint (`/mcp`) is always running. To generate tokens via the Account > MCP Access page, no additional config is needed — tokens are stored in the local database.

### TryDotNet Integration

```
TryDotNet:Origin = https://<trydotnet-origin>
```

### Telemetry

Use one of these — not both simultaneously (they conflict).

```
# Azure Monitor (Application Insights):
APPLICATIONINSIGHTS_CONNECTION_STRING = InstrumentationKey=...

# Local Aspire dashboard (OTLP):
OTEL_EXPORTER_OTLP_ENDPOINT = http://localhost:4317
```

## Production / Staging Secrets

These are only required outside of Development. The app throws at startup if they are missing in non-Development environments.

### Email Sending

```
AuthMessageSender:SendFromName = Hello World Team
AuthMessageSender:SendFromEmail = no-reply@email.com
AuthMessageSender:SecretKey = <mailjet-secret-key>
AuthMessageSender:APIKey = <mailjet-api-key>
```

### Social Login

```
Authentication:Microsoft:ClientId = <client-id>
Authentication:Microsoft:ClientSecret = <client-secret>
Authentication:github:clientId = <client-id>
Authentication:github:clientSecret = <client-secret>
```

### HCaptcha

Development uses [hCaptcha test keys](https://docs.hcaptcha.com/#integration-testing-test-keys) by default. Override for production:

```
HCaptcha:SiteKey = <site-key>
HCaptcha:SecretKey = <secret-key>
```
