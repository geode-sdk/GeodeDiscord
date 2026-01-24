FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GeodeDiscord/GeodeDiscord.csproj", "GeodeDiscord/"]
RUN dotnet restore "GeodeDiscord/GeodeDiscord.csproj"
COPY GeodeDiscord/. ./GeodeDiscord
WORKDIR "/src/GeodeDiscord"
RUN dotnet build "GeodeDiscord.csproj" -c $BUILD_CONFIGURATION --no-restore

FROM build AS efbundle
ARG BUILD_CONFIGURATION=Release
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --no-cache
RUN dotnet ef migrations bundle -p GeodeDiscord.csproj -s GeodeDiscord.csproj --configuration $BUILD_CONFIGURATION --no-build -f -o /app/ef/efbundle

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "GeodeDiscord.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-build

FROM base AS migrations
WORKDIR /app
COPY --from=efbundle /app/ef .
ENTRYPOINT ["efbundle"]

FROM base AS app
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeodeDiscord.dll"]
