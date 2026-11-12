using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PortDeck.Windows;

internal sealed record WindowsUpdateCheckResult(
    string LatestCommit,
    Uri LatestSourceUri,
    WindowsUpdateState State,
    int MainAheadBy = 0,
    int LocalAheadBy = 0);

internal enum WindowsUpdateState
{
    Current,
    UpdateAvailable,
    LocalAhead,
    Diverged,
    Different,
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
                : WindowsUpdateState.Different;

        if (state != WindowsUpdateState.Different || !LooksLikeCommit(localCommit!))
        {
            return new WindowsUpdateCheckResult(latestCommit, latestSourceUri, state);
        }

        var comparisonUri = new Uri(
            $"https://api.github.com/repos/Kaevyx/portdeck/compare/{Uri.EscapeDataString(localCommit!.Trim())}...main");
        using var comparisonResponse = await _httpClient.GetAsync(comparisonUri, cancellationToken);
        if (!comparisonResponse.IsSuccessStatusCode)
        {
            return new WindowsUpdateCheckResult(latestCommit, latestSourceUri, WindowsUpdateState.Different);
        }

        await using var comparisonStream = await comparisonResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var comparisonDocument = await JsonDocument.ParseAsync(
            comparisonStream,
            cancellationToken: cancellationToken);
        var comparisonRoot = comparisonDocument.RootElement;
        var comparisonStatus = comparisonRoot.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;
        var mainAheadBy = ReadCount(comparisonRoot, "ahead_by");
        var localAheadBy = ReadCount(comparisonRoot, "behind_by");

        state = comparisonStatus switch
        {
            "identical" => WindowsUpdateState.Current,
            "ahead" => WindowsUpdateState.UpdateAvailable,
            "behind" => WindowsUpdateState.LocalAhead,
            "diverged" => WindowsUpdateState.Diverged,
            _ => WindowsUpdateState.Different,
        };

        return new WindowsUpdateCheckResult(
            latestCommit,
            latestSourceUri,
            state,
            mainAheadBy,
            localAheadBy);
    }

    private static bool CommitsMatch(string localCommit, string latestCommit)
    {
        var local = localCommit.Trim();
        var latest = latestCommit.Trim();
        return local.Equals(latest, StringComparison.OrdinalIgnoreCase)
               || local.StartsWith(latest, StringComparison.OrdinalIgnoreCase)
               || latest.StartsWith(local, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCommit(string commit)
    {
        var value = commit.Trim();
        return value.Length is >= 7 and <= 40 && value.All(Uri.IsHexDigit);
    }

    private static int ReadCount(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.TryGetInt32(out var value)
            ? value
            : 0;
}
