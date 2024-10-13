using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;


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
        //if (update.Message.Text != null) return;

        try
        {
            await TelegramBotClientHelper.FileExtensionCallbackListenerAsync(botClient, update, Configuration, cancellationToken);

            if (await TelegramBotClientHelper.SendStartMessageAsync(botClient, update, cancellationToken))
                return;

            if (update is not { Type: UpdateType.Message, Message.Text: not null }) return;

            var message = update.Message;
            var chatId = message.Chat.Id;

            if (await Downloader.IsVideoLessTenMinutes(message.Text, cancellationToken: cancellationToken))
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Please, specify the format",
                    replyMarkup: TelegramBotClientHelper.ChooseFileExtensionInlineMarkup(message.Text),
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Uploading started. Please, wait a little",
                    cancellationToken: cancellationToken
                );

                var filePath = await Downloader.DownloadMedia(
                    configuration: Configuration,
                    url: message.Text,
                    isVideo: false,
                    cancellationToken: cancellationToken
                );

                await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var inputFile = new InputFileStream(fileStream, "audio.mp3");
                await botClient.SendAudioAsync(chatId, inputFile, cancellationToken: cancellationToken);
            }
        }
        catch(Exception e)
        {
            if (update.Message is null) return;

            Console.WriteLine(e);

            await botClient.SendTextMessageAsync(
                update.Message.Chat.Id,
                "You Provided invalid link",
                cancellationToken: CancellationToken.None
            );
        }
    }
}


