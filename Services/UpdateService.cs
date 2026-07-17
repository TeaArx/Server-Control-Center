using System.Diagnostics;
using System.Net.Http;
using System.IO;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ServerControlCenter.Models;

namespace ServerControlCenter.Services;

public sealed partial class UpdateService
{
    private const string LatestReleaseEndpoint =
        "https://api.github.com/repos/TeaArx/Server-Control-Center/releases/latest";

    private static readonly HttpClient Client = CreateClient();

    public Version CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseEndpoint, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(content, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("GitHub returned an empty release response.");

        if (!TryParseReleaseVersion(release.TagName, out var releaseVersion) || releaseVersion <= CurrentVersion)
        {
            return null;
        }

        var versionText = $"{releaseVersion.Major}.{releaseVersion.Minor}.{releaseVersion.Build}";
        var expectedInstallerName = $"Server-Control-Center-Setup-{versionText}.exe";
        var installer = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, expectedInstallerName, StringComparison.OrdinalIgnoreCase));
        var checksum = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, expectedInstallerName + ".sha256", StringComparison.OrdinalIgnoreCase));

        if (installer is null || checksum is null ||
            !Uri.TryCreate(installer.DownloadUrl, UriKind.Absolute, out var installerUrl) ||
            !Uri.TryCreate(checksum.DownloadUrl, UriKind.Absolute, out var checksumUrl) ||
            !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releasePageUrl))
        {
            throw new InvalidOperationException("The latest release does not contain the expected installer and checksum.");
        }

        return new UpdateInfo(releaseVersion, installerUrl, checksumUrl, releasePageUrl, expectedInstallerName);
    }

    public async Task<string> DownloadAndVerifyAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var updateDirectory = Path.Combine(
            Path.GetTempPath(),
            "ServerControlCenter",
            "updates",
            update.Version.ToString(3));
        Directory.CreateDirectory(updateDirectory);

        var installerPath = Path.Combine(updateDirectory, update.InstallerFileName);
        var checksumText = await Client.GetStringAsync(update.ChecksumUrl, cancellationToken);
        var expectedHash = ChecksumRegex().Match(checksumText).Value.ToLowerInvariant();

        if (expectedHash.Length != 64)
        {
            throw new InvalidOperationException("The release checksum is missing or invalid.");
        }

        using (var response = await Client.GetAsync(
                   update.InstallerUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var totalLength = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;

            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                if (totalLength > 0)
                {
                    progress?.Report((double)downloaded / totalLength.Value * 100);
                }
            }
        }

        await using var installerStream = File.OpenRead(installerPath);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, cancellationToken))
            .ToLowerInvariant();

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedHash),
                Convert.FromHexString(actualHash)))
        {
            File.Delete(installerPath);
            throw new InvalidOperationException("The downloaded installer failed SHA-256 verification.");
        }

        progress?.Report(100);
        return installerPath;
    }

    public static void StartInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true
        });
    }

    private static bool TryParseReleaseVersion(string tag, out Version version)
    {
        var normalized = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out version!) && version.Build >= 0;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ServerControlCenter", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    [GeneratedRegex("[a-fA-F0-9]{64}", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumRegex();

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("assets")] GitHubAsset[] Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
}