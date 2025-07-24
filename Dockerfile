FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GeodeDiscord/GeodeDiscord.csproj", "GeodeDiscord/"]
COPY GeodeDiscord/. ./GeodeDiscord
WORKDIR "/src/GeodeDiscord"
RUN dotnet publish "GeodeDiscord.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM publish AS migrations
ARG BUILD_CONFIGURATION=Release
RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef database update -p GeodeDiscord.csproj -s GeodeDiscord.csproj --configuration $BUILD_CONFIGURATION --no-build

FROM base AS final
WORKDIR /app
COPY --from=migrations /app/publish .
ENTRYPOINT ["dotnet", "GeodeDiscord.dll"]
