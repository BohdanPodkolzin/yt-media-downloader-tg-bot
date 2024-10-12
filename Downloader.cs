using Microsoft.Extensions.Configuration;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.Diagnostics;

namespace yt_downloader;

public static class Downloader
{
    private static readonly YoutubeClient YoutubeClient = new();

    public static async Task<string> DownloadMedia(IConfiguration configuration, string url, CancellationToken cancellationToken, bool isVideo = true, bool selectLowestBitrate = false)
    {
        var fileName = $"{url.Split('=')[1]}";
        var streamManifest = await YoutubeClient.Videos.Streams.GetManifestAsync(url, cancellationToken);

        var audioStreams = streamManifest.GetAudioStreams();
        IStreamInfo selectedAudioStream = null;

        if (selectLowestBitrate)
        {
            selectedAudioStream = audioStreams
                .OrderBy(s => s.Bitrate)
                .FirstOrDefault();
        }
        else
        {
            selectedAudioStream = audioStreams
                .OrderByDescending(s => s.Bitrate)
                .FirstOrDefault();
        }

        if (selectedAudioStream == null)
        {
            throw new Exception("No audio stream found.");
        }

        var audioFilePath = Path.Combine(configuration["MediaLocalPath"], $"{fileName}_aud.mp4");

        await YoutubeClient.Videos.Streams.DownloadAsync(selectedAudioStream, audioFilePath, null, cancellationToken);

        if (!isVideo) return audioFilePath;

        var videoStreamInfo = streamManifest.GetVideoStreams().First(s => s.VideoQuality.Label == "480p");
        var videoFilePath = Path.Combine(configuration["MediaLocalPath"], $"{fileName}_vid.mp4");

        await YoutubeClient.Videos.Streams.DownloadAsync(videoStreamInfo, videoFilePath, null, cancellationToken);

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
            Arguments = $"-i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a aac b:a  -strict experimental \"{outputPath}\"",
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