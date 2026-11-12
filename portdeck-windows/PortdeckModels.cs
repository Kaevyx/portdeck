using System.Text.Json.Serialization;

namespace PortDeck.Windows;

internal sealed class PortdeckStatus
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "";

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("groups")]
    public List<ProjectGroup> Groups { get; set; } = [];

    [JsonPropertyName("unknown")]
    public List<PortdeckService> Unknown { get; set; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];
}

internal sealed class ProjectGroup
{
    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = "";

    [JsonPropertyName("repoRoot")]
    public string? RepoRoot { get; set; }

    [JsonPropertyName("worktrees")]
    public List<WorktreeGroup> Worktrees { get; set; } = [];
}

internal sealed class WorktreeGroup
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("services")]
    public List<PortdeckService> Services { get; set; } = [];
}

internal sealed class PortdeckService
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("port")]
    public int? Port { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("pid")]
    public int? Pid { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("containerId")]
    public string? ContainerId { get; set; }
}

internal sealed record ServiceRow(
    string Project,
    string Context,
    PortdeckService Service,
    string Folder)
{
    public string Name => Service.Name;
    public string ProjectLabel => Project.ToUpperInvariant();
    public string StatusLabel => string.IsNullOrWhiteSpace(Service.Status)
        ? "UNKNOWN"
        : Service.Status.ToUpperInvariant();
    public string StatusBrush => Service.Status switch
    {
        "running" => "#58D6A8",
        "stale" => "#F5BE62",
        "stopped" => "#FF7D8A",
        _ => "#97A2B4"
    };
    public string StatusTint => Service.Status switch
    {
        "running" => "#182C28",
        "stale" => "#302718",
        "stopped" => "#321D26",
        _ => "#202633"
    };
    public string EndpointLabel => FormatEndpoint(Service);
    public string SourceLabel => Service.Source switch
    {
        "docker" => "DOCKER",
        "process" => "PROCESS",
        _ => Service.Source.ToUpperInvariant()
    };
    public string IdentityLabel => Service.Source == "docker"
        ? ShortContainerIdentity(Service.ContainerId)
        : Service.Pid is int pid ? $"PID {pid}" : "Identity unavailable";
    public string LocationLabel => string.IsNullOrWhiteSpace(Folder) ? "No attributed project folder" : Folder;
    public string ContextLabel => string.IsNullOrWhiteSpace(Context) ? "Unattributed local service" : Context;
    public bool CanOpen => !string.IsNullOrWhiteSpace(Service.Url);
    public bool CanOpenFolder => !string.IsNullOrWhiteSpace(Folder);
    public bool CanStop => Service.Status == "running" &&
                           ((Service.Source == "process" && Service.Pid is > 0) ||
                            (Service.Source == "docker" && !string.IsNullOrWhiteSpace(Service.ContainerId)));

    private static string FormatEndpoint(PortdeckService service)
    {
        if (service.Url is { Length: > 0 } && Uri.TryCreate(service.Url, UriKind.Absolute, out var uri))
        {
            return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        }
        return service.Port is int port ? $"localhost:{port}" : "No endpoint";
    }

    private static string ShortContainerIdentity(string? containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId)) return "Container unavailable";
        return $"CONTAINER {containerId[..Math.Min(containerId.Length, 8)]}";
    }
}

internal sealed class StopActionResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
