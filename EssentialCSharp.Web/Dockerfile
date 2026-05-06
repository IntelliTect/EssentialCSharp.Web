#syntax=docker/dockerfile:1.2

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
RUN --mount=type=secret,id=nugetconfig,required=false \
    if [ "$ACCESS_TO_NUGET_FEED" = "true" ] && [ -f /run/secrets/nugetconfig ]; then \
      mkdir -p ~/.nuget/config && \
      cp /run/secrets/nugetconfig ~/.nuget/config/credentials.config; \
    fi && \
    dotnet restore "EssentialCSharp.Web.slnx" -p:AccessToNugetFeed=$ACCESS_TO_NUGET_FEED && \
    dotnet build "EssentialCSharp.Web.slnx" -c Release --no-restore -p:AccessToNugetFeed=$ACCESS_TO_NUGET_FEED -p:ReleaseDateAttribute=True -p:SkipFrontendBuild=true && \
    dotnet publish "EssentialCSharp.Web.slnx" -c Release -p:PublishDir=/app/publish -p:UseAppHost=false -p:SkipFrontendBuild=true --no-build

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "EssentialCSharp.Web.dll"]
