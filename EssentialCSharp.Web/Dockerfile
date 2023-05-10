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
RUN --mount=type=secret,id=nuget_auth_token nuget_auth_token="$(cat /run/secrets/nuget_auth_token)"
ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://pkgs.dev.azure.com/intelliTect/_packaging/EssentialCSharp/nuget/v3/index.json\", \"username\":\"docker\", \"password\":\"${nuget_auth_token}\"}]}"
RUN dotnet restore "EssentialCSharp.Web.csproj" && \
    dotnet build "EssentialCSharp.Web.csproj" -c Release -o /app/build --no-restore && \
    dotnet publish "EssentialCSharp.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-build

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EssentialCSharp.Web.dll"]
