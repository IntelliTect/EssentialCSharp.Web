# Getting Started with EssentialCSharp.Web Development

This guide will help you set up your local development environment for working on the Essential C# Web project.

## What You Will Need

- [Visual Studio](https://visualstudio.microsoft.com/) (or your preferred IDE)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
  - If you already have .NET installed you can check the version by typing `dotnet --info` into cmd to make sure you have the right version

## Startup Steps

To get the site that is seen at [essentialcsharp.com](https://essentialcsharp.com/):

1. Clone Repository locally.
2. Set any needed secrets
3. If you have do not have access to the private nuget feed, change the line `<AccessToNugetFeed>true</AccessToNugetFeed>` to `<AccessToNugetFeed>false</AccessToNugetFeed>` in [Directory.Packages.props](https://github.com/IntelliTect/EssentialCSharp.Web/blob/main/Directory.Packages.props).

## Environment Prerequisites

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

### Testing Secret Values

Some Value Secrets for Testing/Development Purposes:
HCaptcha: https://docs.hcaptcha.com/#integration-testing-test-keys
