using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PortDeck.Windows;

internal sealed record WindowsUpdateCheckResult(
    string LatestCommit,
    Uri LatestSourceUri,
    WindowsUpdateState State);

internal enum WindowsUpdateState
{
    Current,
    UpdateAvailable,
    ComparisonUnavailable,
}

internal sealed class WindowsUpdateChecker
{
    private static readonly Uri LatestCommitApiUri =
        new("https://api.github.com/repos/Kaevyx/portdeck/commits/main");
    private static readonly Uri RepositoryUri =
        new("https://github.com/Kaevyx/portdeck");

    private readonly HttpClient _httpClient;

    internal WindowsUpdateChecker()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PortDeck-Windows", "0.2"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    internal async Task<WindowsUpdateCheckResult> CheckAsync(
        string? localCommit,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestCommitApiUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var latestCommit = root.TryGetProperty("sha", out var shaElement)
            ? shaElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(latestCommit))
        {
            throw new InvalidDataException("GitHub did not return a commit identifier.");
        }

        var latestSourceUri = RepositoryUri;
        if (root.TryGetProperty("html_url", out var urlElement)
            && Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var commitUri))
        {
            latestSourceUri = commitUri;
        }

        var state = string.IsNullOrWhiteSpace(localCommit)
            ? WindowsUpdateState.ComparisonUnavailable
            : CommitsMatch(localCommit, latestCommit)
                ? WindowsUpdateState.Current
                : WindowsUpdateState.UpdateAvailable;

        return new WindowsUpdateCheckResult(latestCommit, latestSourceUri, state);
    }

    private static bool CommitsMatch(string localCommit, string latestCommit)
    {
        var local = localCommit.Trim();
        var latest = latestCommit.Trim();
        return local.Equals(latest, StringComparison.OrdinalIgnoreCase)
               || local.StartsWith(latest, StringComparison.OrdinalIgnoreCase)
               || latest.StartsWith(local, StringComparison.OrdinalIgnoreCase);
    }
}
