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
RUN dotnet build "GeodeDiscord.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "GeodeDiscord.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM rust:1.81 AS stackwalk
WORKDIR /cargo
RUN cargo install minidump-stackwalk --root .

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=stackwalk /cargo/bin/minidump-stackwalk .
ENTRYPOINT ["dotnet", "GeodeDiscord.dll"]
