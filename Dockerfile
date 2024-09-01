FROM mcr.microsoft.com/dotnet/sdk:8.0

RUN apt-get update && apt-get install -y curl build-essential libssl-dev pkg-config

RUN curl -sSf https://sh.rustup.rs | sh -s -- -y
ENV PATH="/root/.cargo/bin:${PATH}"

RUN cargo install minidump-stackwalk

WORKDIR /app

COPY . .
RUN dotnet restore
RUN dotnet build -c Release

WORKDIR /app
ENV DISCORD_TOKEN=YOURTOKENHERE
ENV DISCORD_TEST_GUILD=0

CMD ["dotnet", "run", "--project", "GeodeDiscord/GeodeDiscord.csproj"]
