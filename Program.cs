using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace yt_downloader;

public class Program
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddUserSecrets<Program>()
        .Build();


    public static async Task Main(string[] args)
    {
        var bot = new TelegramBotClient(Configuration["BotToken"]);

        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = []
        };

        bot.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: cts.Token
        );

        Console.WriteLine("Press any key to exit");
        Console.ReadKey();
        await cts.CancelAsync();
    }

    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage, Console.BackgroundColor == ConsoleColor.DarkRed);
        return Task.CompletedTask;
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cancellationToken)
    {
        //if (update.Message?.Date < DateTime.UtcNow.AddSeconds(-4)) ;
        //{
        //    return;
        //}

        try
        {
            await FileExtensionCallbackListenerAsync(botClient, update, cancellationToken);

            if (await SendStartMessageAsync(botClient, update, cancellationToken))
                return;

            if (update is not { Type: UpdateType.Message, Message.Text: not null }) return;

            var message = update.Message;
            var chatId = message.Chat.Id;


            if (await Downloader.IsVideoLessTenMinutes(message.Text, cancellationToken: cancellationToken))
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Please, specify the format",
                    replyMarkup: ChooseFileExtensionInlineMarkup(message.Text),
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                var filePath = await Downloader.DownloadMedia(
                    configuration: Configuration,
                    url: message.Text,
                    isVideo: false,
                    cancellationToken: cancellationToken
                );

                await botClient.SendAudioAsync(
                    chatId: chatId,
                    audio: new InputFileStream(new FileStream(filePath, FileMode.Open, FileAccess.Read)),
                    cancellationToken: cancellationToken
                );
            }
        }
        catch
        {
            if (update.Message is null) return;

            await botClient.SendTextMessageAsync(
                update.Message.Chat.Id,
                "You Provided invalid link",
                cancellationToken: CancellationToken.None
            );
        }
    }

    private static InlineKeyboardMarkup ChooseFileExtensionInlineMarkup(string url)
    {
        return new InlineKeyboardMarkup(
            new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData(".mp3", $"mp3_choice|{url}") },
                new[] { InlineKeyboardButton.WithCallbackData(".mp4", $"mp4_choice|{url}") }
            });
    }

    private static async Task FileExtensionCallbackListenerAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
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
        ); ;

        var filePath = isMp3Format
            ? await Downloader.DownloadMedia(
                configuration: Configuration,
                url: url,
                isVideo: false,
                cancellationToken: cancellationToken
            )
            : await Downloader.DownloadMedia(
                configuration: Configuration,
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

    private static async Task<bool> SendStartMessageAsync(ITelegramBotClient botClient, Update update,
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
}


