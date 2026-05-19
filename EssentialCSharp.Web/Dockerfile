# syntax=docker/dockerfile:1.4

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM node:26-bookworm-slim AS frontend-build
WORKDIR /frontend/EssentialCSharp.Web
COPY EssentialCSharp.Web/package.json EssentialCSharp.Web/package-lock.json ./
RUN --mount=type=cache,id=essentialcsharp-web-npm,target=/root/.npm \
    npm ci
COPY EssentialCSharp.Web/vite.config.js ./vite.config.js
COPY EssentialCSharp.Web/src ./src
COPY EssentialCSharp.Web/wwwroot/js ./wwwroot/js
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
ARG ACCESS_TO_NUGET_FEED=true
ENV ACCESS_TO_NUGET_FEED=$ACCESS_TO_NUGET_FEED
WORKDIR /src
COPY EssentialCSharp.Web.slnx ./
COPY Directory.Build.props Directory.Build.targets Directory.Packages.props global.json nuget.config ./
COPY EssentialCSharp.Chat.Shared/EssentialCSharp.Chat.Common.csproj ./EssentialCSharp.Chat.Shared/
COPY EssentialCSharp.Chat.Tests/EssentialCSharp.Chat.Tests.csproj ./EssentialCSharp.Chat.Tests/
COPY EssentialCSharp.Chat/EssentialCSharp.Chat.csproj ./EssentialCSharp.Chat/
COPY EssentialCSharp.Web.Tests/EssentialCSharp.Web.Tests.csproj ./EssentialCSharp.Web.Tests/
COPY EssentialCSharp.Web/EssentialCSharp.Web.csproj ./EssentialCSharp.Web/
RUN --mount=type=secret,id=nuget_pat,required=false \
    --mount=type=cache,id=essentialcsharp-web-nuget,target=/root/.nuget/packages \
    if [ "$ACCESS_TO_NUGET_FEED" = "true" ] && [ ! -s /run/secrets/nuget_pat ]; then \
      echo "ERROR: ACCESS_TO_NUGET_FEED=true but nuget_pat secret is missing or empty" >&2; exit 1; \
    fi && \
    if [ "$ACCESS_TO_NUGET_FEED" = "true" ]; then \
      mkdir -p /root/.nuget/NuGet && \
      printf '<?xml version="1.0" encoding="utf-8"?>\n<configuration>\n  <packageSourceCredentials>\n    <EssentialCSharp>\n      <add key="Username" value="az" />\n      <add key="ClearTextPassword" value="%s" />\n    </EssentialCSharp>\n  </packageSourceCredentials>\n</configuration>\n' \
        "$(cat /run/secrets/nuget_pat)" > /root/.nuget/NuGet/NuGet.Config; \
    fi && \
    dotnet restore "EssentialCSharp.Web.slnx" -p:AccessToNugetFeed=$ACCESS_TO_NUGET_FEED && \
    rm -f /root/.nuget/NuGet/NuGet.Config
COPY . .
COPY --from=frontend-build /frontend/EssentialCSharp.Web/wwwroot/dist ./EssentialCSharp.Web/wwwroot/dist
RUN --mount=type=cache,id=essentialcsharp-web-nuget,target=/root/.nuget/packages \
    dotnet build "EssentialCSharp.Web.slnx" -c Release --no-restore -p:AccessToNugetFeed=$ACCESS_TO_NUGET_FEED -p:ReleaseDateAttribute=True -p:SkipFrontendBuild=true
RUN --mount=type=cache,id=essentialcsharp-web-nuget,target=/root/.nuget/packages \
    dotnet publish "EssentialCSharp.Web.slnx" -c Release -p:PublishDir=/app/publish -p:UseAppHost=false -p:SkipFrontendBuild=true --no-build

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "EssentialCSharp.Web.dll"]
