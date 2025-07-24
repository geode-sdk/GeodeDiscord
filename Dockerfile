FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS premigrations
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef
RUN dotnet tool update --global dotnet-ef

FROM premigrations AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GeodeDiscord/GeodeDiscord.csproj", "GeodeDiscord/"]
RUN dotnet restore "GeodeDiscord/GeodeDiscord.csproj"
COPY GeodeDiscord/. ./GeodeDiscord
WORKDIR "/src/GeodeDiscord"
RUN dotnet build "GeodeDiscord.csproj" -c $BUILD_CONFIGURATION --no-restore

FROM build AS migrations
ARG BUILD_CONFIGURATION=Release
RUN dotnet ef database update -p GeodeDiscord.csproj -s GeodeDiscord.csproj --configuration $BUILD_CONFIGURATION --no-build

FROM migrations AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "GeodeDiscord.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-build

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeodeDiscord.dll"]
