#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ../ .
WORKDIR "/src/EssentialCSharp.Web"
RUN dotnet restore "EssentialCSharp.Web.csproj" && \
    dotnet build "EssentialCSharp.Web.csproj" -c Release -o /app/build --no-restore && \
    dotnet publish "EssentialCSharp.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-build

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EssentialCSharp.Web.dll"]