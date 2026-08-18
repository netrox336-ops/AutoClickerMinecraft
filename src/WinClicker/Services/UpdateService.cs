using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace WinClicker.Services;

internal sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string Name);

internal sealed class UpdateService
{
    internal const string Repository = "netrox336-ops/AutoClickerMinecraft";
    private static readonly HttpClient Client = CreateClient();

    internal async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(
            $"https://api.github.com/repos/{Repository}/releases/latest",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? string.Empty
            : string.Empty;
        var page = root.TryGetProperty("html_url", out var pageElement)
            ? pageElement.GetString() ?? string.Empty
            : string.Empty;
        var name = root.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString() ?? tag
            : tag;

        if (!TryParseVersion(tag, out var latest))
        {
            throw new InvalidDataException("GitHub Release содержит неподдерживаемый номер версии.");
        }

        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(3, 0, 1);
        return latest > current ? new UpdateInfo(latest, tag, page, name) : null;
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var normalized = tag.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var dash = normalized.IndexOf('-');
        if (dash >= 0)
        {
            normalized = normalized[..dash];
        }

        return Version.TryParse(normalized, out version!);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AutoClicker", "3.0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
