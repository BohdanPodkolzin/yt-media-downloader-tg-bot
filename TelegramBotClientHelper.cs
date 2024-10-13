using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace yt_downloader
{
    public static class TelegramBotClientHelper
    {

        public static async Task<bool> SendStartMessageAsync(ITelegramBotClient botClient, Update update,
            CancellationToken cancellationToken)
        {
            if (update.Message is not { Text: "/start" }) return false;

            await botClient.SendTextMessageAsync(
                update.Message.Chat.Id,
                $"Hi, @{update.Message.From?.Username}! Provide YouTube link to download video. " +
                $"Up to 10 minutes you could download video with audio," +
                $" over 10 minutes - only audio",
                cancellationToken: cancellationToken
            );

            return true;
        }

        public static async Task FileExtensionCallbackListenerAsync(ITelegramBotClient botClient, Update update, IConfiguration configuration, CancellationToken cancellationToken)
        {
            if (update is not { Type: UpdateType.CallbackQuery, CallbackQuery: not null }) return;

            if (update.CallbackQuery.Message == null) return;

            var chatIdCallback = update.CallbackQuery.Message.Chat.Id;

            var callbackData = update.CallbackQuery.Data?.Split('|');

            if (callbackData is not { Length: 2 }) return;

            var formatChoice = callbackData[0];
            var url = callbackData[1];

            var isMp3Format = formatChoice == "mp3_choice";

            await botClient.EditMessageTextAsync(
                chatId: chatIdCallback,
                messageId: update.CallbackQuery.Message.MessageId,
                text: "Uploading started. Please, wait a little",
                cancellationToken: cancellationToken
            );

            var filePath = isMp3Format
                ? await Downloader.DownloadMedia(
                    configuration: configuration,
                    url: url,
                    isVideo: false,
                    cancellationToken: cancellationToken
                )
                : await Downloader.DownloadMedia(
                    configuration: configuration,
                    url: url,
                    cancellationToken: cancellationToken
                );

            _ = isMp3Format
                ? await botClient.SendAudioAsync(
                    chatIdCallback,
                    audio: new InputFileStream(new FileStream(filePath, FileMode.Open, FileAccess.Read)),
                    cancellationToken: cancellationToken
                )
                : await botClient.SendVideoAsync(
                    chatIdCallback,
                    video: new InputFileStream(new FileStream(filePath, FileMode.Open, FileAccess.Read)),
                    supportsStreaming: true,
                    cancellationToken: cancellationToken
                );

            await botClient.DeleteMessageAsync(
                chatId: chatIdCallback,
                messageId: update.CallbackQuery.Message.MessageId,
                cancellationToken: cancellationToken
            );

            await botClient.AnswerCallbackQueryAsync(update.CallbackQuery.Id, cancellationToken: cancellationToken);
        }

        public static InlineKeyboardMarkup ChooseFileExtensionInlineMarkup(string url)
        {
            return new InlineKeyboardMarkup(
                new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData(".mp3", $"mp3_choice|{url}") },
                    [InlineKeyboardButton.WithCallbackData(".mp4", $"mp4_choice|{url}")]
                });
        }
    }
}
