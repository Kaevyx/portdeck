using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PortDeck.Windows;

internal sealed class PortdeckClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _nodePath;
    private readonly string _cliPath;

    public PortdeckClient()
    {
        _nodePath = Environment.GetEnvironmentVariable("PORTDECK_NODE") ?? "node.exe";
        var explicitCli = Environment.GetEnvironmentVariable("PORTDECK_CLI");
        _cliPath = !string.IsNullOrWhiteSpace(explicitCli) ? explicitCli : FindDevelopmentCli();
    }

    public async Task<PortdeckStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var json = await RunAsync(["status", "--json"], cancellationToken);
        return JsonSerializer.Deserialize<PortdeckStatus>(json, JsonOptions)
               ?? throw new InvalidOperationException("PortDeck returned an empty status document.");
    }

    public async Task<StopActionResult> StopAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        var json = await RunAsync(["stop", "--service-id", serviceId, "--json"], cancellationToken, allowFailure: true);
        return JsonSerializer.Deserialize<StopActionResult>(json, JsonOptions)
               ?? new StopActionResult { Message = "PortDeck returned an empty stop result." };
    }

    private async Task<string> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowFailure = false)
    {
        if (!File.Exists(_cliPath))
        {
            throw new FileNotFoundException("Build the PortDeck helper with `npm run build:app` before starting the Windows app.", _cliPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _nodePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_cliPath)!
        };
        startInfo.ArgumentList.Add(_cliPath);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start the PortDeck helper.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                ? "PortDeck helper failed."
                : stderr.Trim());
        }
        return stdout;
    }

    private static string FindDevelopmentCli()
    {
        var explicitRoot = Environment.GetEnvironmentVariable("PORTDECK_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.Combine(explicitRoot, "portdeck-app", "dist", "cli.js");
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "portdeck-app", "dist", "cli.js");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "portdeck-app", "dist", "cli.js");
    }
}
