using Microsoft.Extensions.Configuration;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.Diagnostics;

namespace yt_downloader;

public static class Downloader
{
    private static readonly YoutubeClient YoutubeClient = new();

    public static async Task<string> DownloadMedia(IConfiguration configuration, string url, CancellationToken cancellationToken, bool isVideo = true)
    {
        var fileName = $"{url.Split('=')[1]}";

        var streamManifest = await YoutubeClient.Videos.Streams.GetManifestAsync(url, cancellationToken);

        // audio downloader
        var audioStreamInfo = streamManifest.GetAudioStreams().GetWithHighestBitrate();
        var audioFilePath = Path.Combine(configuration["MediaLocalPath"], $"{fileName}_aud.mp4");

        await YoutubeClient.Videos.Streams.DownloadAsync(audioStreamInfo, audioFilePath, cancellationToken: cancellationToken);
            
        if (!isVideo) return audioFilePath;

        // video downloader
        var videoStreamInfo = streamManifest.GetVideoStreams().First(s => s.VideoQuality.Label == "480p");
        var videoFilePath = Path.Combine(configuration["MediaLocalPath"], $"{fileName}_vid.mp4");

        await YoutubeClient.Videos.Streams.DownloadAsync(videoStreamInfo, videoFilePath, cancellationToken: cancellationToken);

        var finalFilePath = Path.Combine(configuration["MediaLocalPath"], $"{fileName}.mp4");
            
        if (File.Exists(finalFilePath)) return finalFilePath;

        await MergeAudioVideoAsync(configuration, audioFilePath, videoFilePath, finalFilePath);

        File.Delete(audioFilePath);
        File.Delete(videoFilePath);

        return finalFilePath;
    }
    private static async Task MergeAudioVideoAsync(IConfiguration configuration, string audioPath, string videoPath, string outputPath)
    {
        var ffmpegPath = $"{configuration["FfmpegPath"]}";

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a aac -strict experimental \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);

        if (process == null) return;
        
        _ = ReadStreamAsync(process.StandardOutput);
        _ = ReadStreamAsync(process.StandardError);

        await Task.Run(() => process.WaitForExit());
        
    }

    private static async Task ReadStreamAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is not null)
        {
        }
    }

    public static async Task<bool> IsVideoLessTenMinutes(string url, CancellationToken cancellationToken) 
        => (await YoutubeClient.Videos.GetAsync(url, cancellationToken)).Duration <= TimeSpan.FromMinutes(11);
}