FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /app

COPY . .
RUN dotnet restore
RUN dotnet build -c Release

WORKDIR /app
ENV DISCORD_TOKEN=YOURTOKENHERE
ENV DISCORD_TEST_GUILD=0

CMD ["dotnet", "run", "--project", "GeodeDiscord/GeodeDiscord.csproj"]
