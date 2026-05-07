# syntax=docker/dockerfile:1.4

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM node:24-bookworm-slim AS frontend-build
WORKDIR /frontend/EssentialCSharp.Web
COPY EssentialCSharp.Web/package.json EssentialCSharp.Web/package-lock.json ./
RUN npm ci
COPY EssentialCSharp.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS build
ARG ACCESS_TO_NUGET_FEED=true
ENV ACCESS_TO_NUGET_FEED=$ACCESS_TO_NUGET_FEED
WORKDIR /src
COPY . .
COPY --from=frontend-build /frontend/EssentialCSharp.Web/wwwroot/dist ./EssentialCSharp.Web/wwwroot/dist
RUN --mount=type=secret,id=nuget_pat,required=false \
    if [ "$ACCESS_TO_NUGET_FEED" = "true" ] && [ ! -f /run/secrets/nuget_pat ]; then \
      echo "ERROR: ACCESS_TO_NUGET_FEED=true but nuget_pat secret is missing" >&2; exit 1; \
    fi && \
    cp nuget.config nuget.config.bak && \
    if [ "$ACCESS_TO_NUGET_FEED" = "true" ]; then \
      dotnet nuget update source EssentialCSharp \
        --username az \
        --password "$(cat /run/secrets/nuget_pat)" \
        --store-password-in-clear-text; \
    fi && \
    dotnet restore "EssentialCSharp.Web.slnx" -p:AccessToNugetFeed=$ACCESS_TO_NUGET_FEED && \
    dotnet build "EssentialCSharp.Web.slnx" -c Release --no-restore -p:AccessToNugetFeed=$ACCESS_TO_NUGET_FEED -p:ReleaseDateAttribute=True -p:SkipFrontendBuild=true && \
    dotnet publish "EssentialCSharp.Web.slnx" -c Release -p:PublishDir=/app/publish -p:UseAppHost=false -p:SkipFrontendBuild=true --no-build && \
    mv nuget.config.bak nuget.config

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "EssentialCSharp.Web.dll"]
