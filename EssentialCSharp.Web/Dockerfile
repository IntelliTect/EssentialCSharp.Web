#syntax=docker/dockerfile:1.2

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
RUN sh -c "$(curl -fsSL https://aka.ms/install-artifacts-credprovider.sh)"
WORKDIR /src
COPY . .
RUN --mount=type=secret,id=nuget_auth_token \
    auth_token=$(cat /run/secrets/nuget_auth_token) && \
    export VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/intelliTect/_packaging/EssentialCSharp/nuget/v3/index.json\", \"password\":\"$auth_token\"}]}" && \
    dotnet restore "EssentialCSharp.Web.sln" && \
    dotnet build "EssentialCSharp.Web.sln" -c Release --no-restore && \
    dotnet publish "EssentialCSharp.Web.sln" -c Release -p:PublishDir=/app/publish -p:UseAppHost=false --no-build

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EssentialCSharp.Web.dll"]
