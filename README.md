# Geode Bot
A bot for the Geode SDK Discord Server

# Prerequisites
- DotNET SDK (8.0)
- Rust (For minidump-stackwalk)
- Discord Bot
- Discord Server

# Setup
```
cargo install minidump-stackwalk
git clone https://github.com/geode-sdk/GeodeDiscord
dotnet build -c Release
dotnet run --project GeodeDiscord/GeodeDiscord.csproj
```

# Environment Variables
- `DISCORD_TOKEN` - The Discord Token for running the bot
- `DISCORD_TEST_GUILD` - The "test" guild for the bot. Also creates slash commands for the guild if it hadn't been created already.
