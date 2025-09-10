# Essential C# Web Project

## Projects Overview

- [EssentialCSharp.Web](https://github.com/IntelliTect/EssentialCSharp.Web/tree/main/EssentialCSharp.Web) - The site seen at [essentialcsharp.com](https://essentialcsharp.com/)

For any bugs, questions, or anything else with specifically the code found inside the listings (listing examples code), please submit an issue at the [EssentialCSharp Repo](https://github.com/IntelliTect/EssentialCSharp).

## What You Will Need

- [Visual Studio](https://visualstudio.microsoft.com/) (or your preferred IDE)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
  - If you already have .NET installed you can check the version by typing `dotnet --info` into cmd to make sure you have the right version

## Startup Steps

To get the site that is seen at [essentialcsharp.com](https://essentialcsharp.com/):

1. Clone Repository locally.
2. Set any needed secrets (see Environment Prerequisites below)
3. If you do not have access to the private nuget feed, change the line `<AccessToNugetFeed>true</AccessToNugetFeed>` to `<AccessToNugetFeed>false</AccessToNugetFeed>` in [Directory.Packages.props](https://github.com/IntelliTect/EssentialCSharp.Web/blob/main/Directory.Packages.props).

## Development Container Setup

This project includes a VS Code devcontainer for consistent development environments. To use it:

### With Private NuGet Feed Access

If you have access to the IntelliTect private Azure DevOps NuGet feed:

1. Create an Azure DevOps Personal Access Token (PAT) with **Packaging (Read)** permissions
2. Set the environment variable before opening the devcontainer:
   ```bash
   export AZURE_DEVOPS_PAT="your-azure-devops-pat-token"
   ```
   Or add it to your shell profile (`.bashrc`, `.zshrc`, etc.)
3. Open the project in VS Code and select "Reopen in Container" when prompted

### Without Private NuGet Feed Access

If you don't have access to the private feed:

1. Update `Directory.Packages.props` and set `<AccessToNugetFeed>false</AccessToNugetFeed>`
2. Open the project in VS Code and select "Reopen in Container" when prompted

The devcontainer will automatically:
- Install the correct .NET 9.0 SDK
- Configure NuGet authentication (if PAT token is provided)
- Restore all packages
- Set up the development environment

## Environment Prerequisites

### Application Secrets

Make sure the following secrets are set:
In local development this ideally should be done using the dotnet secret manager. Additional information can be found at the [documentation](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets#set-a-secret)

AuthMessageSender:SendFromName = "Hello World Team"
AuthMessageSender:SendFromEmail = "no-reply@email.com"
AuthMessageSender:SecretKey = alongstringofsecretsauce
AuthMessageSender:APIKey = anapikey
Authentication:Microsoft:ClientSecret = anotherimportantsecret
Authentication:Microsoft:ClientId = anotherimportantclient
Authentication:github:clientSecret = anotherimportantclientsecret
Authentication:github:clientId = anotherimportantclientid
HCaptcha:SiteKey = captchaSiteKey
HCaptcha:SecretKey = captchaSecretKey
APPLICATIONINSIGHTS_CONNECTION_STRING = "InstrumentationKey=your-instrumentation-key-here;IngestionEndpoint=https://region.in.applicationinsights.azure.com/;LiveEndpoint=https://region.livediagnostics.monitor.azure.com/"

### Private NuGet Feed Access

For developers with access to IntelliTect's private Azure DevOps NuGet feed, you'll need to set up authentication:

**For DevContainer/Local Development:**
Set the `AZURE_DEVOPS_PAT` environment variable with your Azure DevOps Personal Access Token:
```bash
export AZURE_DEVOPS_PAT="your-azure-devops-pat-token"
```

**Creating an Azure DevOps PAT:**
1. Go to Azure DevOps → User Settings → Personal Access Tokens
2. Create a new token with **Packaging (Read)** permissions
3. Copy the token and set it as the environment variable above

**Without Private Feed Access:**
If you don't have access to the private feed, set `<AccessToNugetFeed>false</AccessToNugetFeed>` in `Directory.Packages.props`

Testing Secret Values:
Some Value Secrets for Testing/Development Purposes:
HCaptcha: https://docs.hcaptcha.com/#integration-testing-test-keys

Please use issues or discussions to report issues found.
