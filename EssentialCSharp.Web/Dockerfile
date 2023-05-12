# syntax = docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
RUN curl -L https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh  | sh
WORKDIR /src
COPY EssentialCSharp.Web/*.csproj .
COPY nuget.config .
RUN --mount=type=secret,id=nuget_auth_token \
    cat /run/secrets/nuget_auth_token && \
    echo we are here && \
    export VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/intelliTect/_packaging/EssentialCSharp/nuget/v3/index.json\", \"password\":"$(cat /run/secrets/nuget_auth_token)"}]}" \
    echo $VSS_NUGET_EXTERNAL_FEED_ENDPOINTS && \
    dotnet restore "EssentialCSharp.Web.csproj" -v d && \
    dotnet build "EssentialCSharp.Web.csproj" -c Release -o /app/build --no-restore -v d && \
    dotnet publish "EssentialCSharp.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-build -v d

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EssentialCSharp.Web.dll"]
