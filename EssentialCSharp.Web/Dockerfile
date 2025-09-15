#syntax=docker/dockerfile:1.2

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0.305 AS build
ARG ACCESS_TO_NUGET_FEED=true
ENV ACCESS_TO_NUGET_FEED=$ACCESS_TO_NUGET_FEED
RUN sh -c "$(curl -fsSL https://aka.ms/install-artifacts-credprovider.sh)"
WORKDIR /src
COPY . .
RUN --mount=type=secret,id=nuget_auth_token \
    if [ "$ACCESS_TO_NUGET_FEED" = "true" ]; then \
      auth_token=$(cat /run/secrets/nuget_auth_token) && \
      export VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/intelliTect/_packaging/EssentialCSharp/nuget/v3/index.json\", \"password\":\"$auth_token\"}]}"; \
    fi && \
    dotnet restore "EssentialCSharp.Web.sln" -p:AccessToNugetFeed=$ACCESS_TO_NUGET_FEED && \
    dotnet build "EssentialCSharp.Web.sln" -c Release --no-restore -p:AccessToNugetFeed=$ACCESS_TO_NUGET_FEED -p:ReleaseDateAttribute=True && \
    dotnet publish "EssentialCSharp.Web.sln" -c Release -p:PublishDir=/app/publish -p:UseAppHost=false --no-build

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EssentialCSharp.Web.dll"]
