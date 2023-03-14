# Essential C# Web Project

## Projects Overview

[EssentialCSharp.Web](https://github.com/IntelliTect/EssentialCSharpManuscript/tree/main/Utilities/EssentialCSharp.Web) - The site seen at [essentialcsharp.com](https://essentialcsharp.com/)

## What You Will Need

- [Visual Studio](https://visualstudio.microsoft.com/) (or your preferred IDE)
- [.NET 7.0 SDK](https://dotnet.microsoft.com/en-us/download)
  - If you already have .NET installed you can check the version by typing `dotnet --info` into cmd to make sure you have the right version

## Startup Steps

To get the site that is seen at [essentialcsharp.com](https://essentialcsharp.com/):

1. Clone Repository locally.
2. If you have do not have access to the private nuget feed, change the line `<AccessToNugetFeed>true</AccessToNugetFeed>` to `<AccessToNugetFeed>false</AccessToNugetFeed>` in [EssentialCSharp.Web/EssentialCSharp.Web.csproj](https://github.com/IntelliTect/EssentialCSharp.Web/blob/main/EssentialCSharp.Web/EssentialCSharp.Web.csproj)
