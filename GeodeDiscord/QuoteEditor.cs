using Discord;
using Discord.Interactions;
using Discord.Net.Converters;
using Discord.Rest;
using GeodeDiscord.Database;
using GeodeDiscord.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Serilog;

namespace GeodeDiscord;

public class QuoteEditor(ApplicationDbContext db, SocketInteractionContext context) {
    public async Task<Quote> Create(IMessage message) {
        IMessageChannel? channel = await context.Interaction.GetChannelAsync();
        if (channel is not null) {
            IMessage? realMessage = await channel.GetMessageAsync(message.Id);
            if (realMessage is not null)
                message = realMessage;
        }

        if (db.quotes.Any(q => q.messageId == message.Id)) {
            throw new MessageErrorException("This message is already quoted!");
        }

        return await MessageToQuote(context.User.Id, 0, message, context.Interaction.CreatedAt);
    }

    public async Task<Quote> Add(Quote quote) {
        // ensure id is still unique
        int max = !await db.quotes.AnyAsync() ? 0 : await db.quotes.Select(x => x.id).MaxAsync();
        quote = quote with { id = max + 1 };
        db.Add(quote);

        LogOnSave(() => Log.Information(
            "{User} added quote {Id} from {MessageId}",
            context.User.Id, quote.GetFullName(), quote.messageId
        ));

        return quote;
    }

    public void Rename(Quote quote, string newName) {
        string oldName = quote.GetFullName();
        quote.name = newName.Trim();

        LogOnSave(() => Log.Information(
            "{User} renamed quote {OldName} to {NewName}",
            context.User.Id, oldName, quote.GetFullName()
        ));
    }

    public void Delete(Quote quote) {
        db.Remove(quote);

        LogOnSave(() => Log.Information(
            "{User} deleted quote {Name}",
            context.User.Id, quote.GetFullName()
        ));
    }

    public async Task Update(Quote quote) {
        if (quote.channelId == 0)
            throw new MessageErrorException("Failed to update quote! (channel ID not set)");

        IMessageChannel? channel = context.Guild.GetTextChannel(quote.channelId) ??
            context.Guild.GetStageChannel(quote.channelId) ??
            context.Guild.GetVoiceChannel(quote.channelId);
        if (channel is null)
            throw new MessageErrorException($"Failed to update quote! (channel {quote.channelId} not found)");

        IMessage? message = await channel.GetMessageAsync(quote.messageId);
        if (message is null)
            throw new MessageErrorException($"Failed to update quote! (message {quote.messageId} not found)");

        Update(quote, await MessageToQuote(quote.quoterId, quote.id, message, context.Interaction.CreatedAt, quote));
    }

    public void Update(Quote oldQuote, Quote newQuote) {
        if (oldQuote.messageId != newQuote.messageId)
            throw new InvalidOperationException("Cannot change message ID of a quote");
        db.Remove(oldQuote);
        db.Add(newQuote);

        LogOnSave(() => Log.Information(
            "{User} updated quote {OldName} to {NewName}",
            context.User.Id, oldQuote.GetFullName(), newQuote.GetFullName()
        ));
    }

    private int _onSaveLogCount;
    private void LogOnSave(Action log) {
        // prevent log spam in quote-import
        if (_onSaveLogCount >= 10)
            return;
        db.SavedChanges += SuccessHandler;
        db.SaveChangesFailed += FailHandler;
        _onSaveLogCount++;
        return;

        void SuccessHandler(object? sender, SavedChangesEventArgs args) {
            log.Invoke();
            db.SavedChanges -= SuccessHandler;
            db.SaveChangesFailed -= FailHandler;
            _onSaveLogCount--;
        }
        void FailHandler(object? sender, SaveChangesFailedEventArgs args) {
            db.SavedChanges -= SuccessHandler;
            db.SaveChangesFailed -= FailHandler;
            _onSaveLogCount--;
        }
    }

    private static async Task<IMessage?> GetForwardedAsync(IMessage message) {
        if (message.Channel is null || message.Reference is null ||
            message.Reference.ChannelId != message.Channel.Id ||
            !message.Reference.MessageId.IsSpecified ||
            message.Reference.ReferenceType.GetValueOrDefault() != MessageReferenceType.Forward)
            return null;
        ulong refMessageId = message.Reference.MessageId.Value;
        IMessage? refMessage = await message.Channel.GetMessageAsync(refMessageId);
        return refMessage ?? null;
    }

    private static async Task<Quote> MessageToQuote(ulong quoterId, int id, IMessage message,
        DateTimeOffset timestamp, Quote? original = null) {
        while (true) {
            // if we're just quoting a forwarded message, quote the forwarded message instead
            IMessage? forwarded = await GetForwardedAsync(message);
            if (forwarded is not null) {
                message = forwarded;
                continue;
            }

            IMessage? reply = await Util.GetReplyAsync(message);
            return new Quote {
                id = id,
                name = original?.name ?? "",
                messageId = message.Id,
                channelId = message.Channel?.Id ?? 0,
                createdAt = original?.createdAt ?? timestamp,
                lastEditedAt = timestamp,
                quoterId = quoterId,
                authorId = message.Author.Id,
                jumpUrl = message.Channel is null ? null : message.GetJumpUrl(),
                content = message.Content,
                components = await MessageComponentsToQuote(message),
                attachments = MessageAttachmentsToQuote(message),
                embeds = MessageEmbedsToQuote(message),
                replyAuthorId = reply?.Author.Id ?? 0,
                replyMessageId = reply?.Id ?? 0,
                replyContent = reply?.Content ?? ""
            };
        }
    }

    public static async Task<byte[]> MessageComponentsToQuote(IMessage message) {
        if (message.Components.Count <= 0)
            return [];
        using MemoryStream stream = new();
        await using BsonDataWriter bson = new(stream);
        JsonSerializer.Create(new JsonSerializerSettings {
            ContractResolver = new DiscordContractResolver()
        }).Serialize(bson, new Quote.FakeMessage {
            components = message.Components.Select(x => x.ToModel()).ToArray()
        });
        return stream.ToArray();
    }

    public static ICollection<Quote.Attachment> MessageAttachmentsToQuote(IMessage message) {
        return [..message.Attachments.Select(x => new Quote.Attachment {
            id = x.Id,
            name = string.IsNullOrWhiteSpace(x.Title) ? x.Filename : x.Title + Path.GetExtension(x.Filename),
            size = x.Size,
            url = x.Url,
            contentType = x.ContentType,
            description = x.Description,
            isSpoiler = x.Filename.StartsWith("SPOILER_") // seems to be the actual condition
        })];
    }

    public static ICollection<Quote.Embed> MessageEmbedsToQuote(IMessage message) {
        return [..message.Embeds.Select(x => new Quote.Embed {
            type = x.Type,
            color = x.Color,
            providerName = x.Provider?.Name,
            providerUrl = x.Provider?.Url,
            authorIconUrl = x.Author?.IconUrl,
            authorName = x.Author?.Name,
            authorUrl = x.Author?.Url,
            title = x.Title,
            url = x.Url,
            thumbnailUrl = x.Thumbnail?.Url,
            description = x.Description,
            fields = [..x.Fields.Select(y => new Quote.Embed.Field {
                name = y.Name,
                value = y.Value,
                inline = y.Inline
            })],
            videoUrl = x.Video?.Url,
            imageUrl = x.Image?.Url,
            footerIconUrl = x.Footer?.IconUrl,
            footerText = x.Footer?.Text,
            timestamp = x.Timestamp
        })];
    }
}
