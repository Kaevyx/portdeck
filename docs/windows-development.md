# Windows Development Preview

PortDeck for Windows is a local-development preview that consumes the same `portdeck status --json` contract as the macOS app. The first milestone covers local listening processes, Git attribution when Windows exposes a usable project path, Docker services, endpoint opening, folder opening, activity metadata, and confirmed process or container stops.

Remote deployment-provider views are not part of this first Windows milestone. The existing macOS provider adapters remain unchanged.

## Requirements

- Windows 10 or Windows 11
- Node.js and npm
- .NET 10 SDK
- Git
- Docker Desktop when Docker discovery is wanted

Install the .NET SDK when `dotnet --list-sdks` is empty:

```powershell
winget install Microsoft.DotNet.SDK.10
```

## Run locally

From the repository root:

```powershell
npm ci
npm run run:windows
```

The launcher builds the TypeScript helper, starts the WPF tray application, and opens the dashboard for development. Select the PortDeck tray icon to show or hide the dashboard. Right-click it for Refresh and Exit actions.

The dashboard behaves as a taskbar flyout:

- It anchors to the taskbar edge on the monitor where it is opened and repositions after display or taskbar changes.
- It cannot be dragged away from the taskbar edge.
- It tucks back into the tray when it loses focus unless **Keep flyout open** is enabled.
- The header pin toggles that behavior without opening Settings.
- The clearly labelled **Settings** button provides Dark, Light, and System themes. Dark is the default; System reads the Windows app theme at startup and when Windows preferences change.
- Settings shows the Windows preview version and the source commit used by the development launcher.
- Preferences are stored at `%LOCALAPPDATA%\PortDeck\windows-settings.json` and contain no project or provider data.

On enterprise-managed Windows devices, Application Control may require the development executable to be signed by an organization-approved certificate. PortDeck does not bypass that policy; use an approved development-signing workflow, request an administrator-managed allow rule for PortDeck development builds, or test on a device that permits locally built applications. The launcher detects a matching Code Integrity event and reports these options explicitly.

The source-development app resolves:

- `PORTDECK_NODE` as an optional authoritative Node executable override.
- `PORTDECK_CLI` as an optional authoritative compiled helper override.
- `PORTDECK_REPO_ROOT` as the source checkout containing `portdeck-app/dist/cli.js`.

The launcher sets `PORTDECK_REPO_ROOT` automatically. Invalid explicit executable or helper overrides fail visibly instead of falling through.

## Windows discovery boundary

The shared TypeScript helper invokes a non-interactive Windows PowerShell snapshot using built-in `Get-NetTCPConnection` and CIM process providers. It does not require administrator access. Local process discovery is limited to the current interactive Windows session so system-service listeners do not overwhelm the development dashboard. Access-controlled processes may omit command or activity metadata and remain in **Unknown services** rather than failing the full snapshot.

Windows does not expose another process's current working directory through a stable public API. For the preview, PortDeck assigns a project only when the process command contains an existing absolute script or application path. Docker label, mount, and Compose-path attribution continues to use the existing stronger rules. Services without safe path evidence remain explicitly unknown.

Confirmed process stops reuse the existing status-refresh identity check. On Windows, Node maps the requested termination signal to Windows process termination; Docker services continue to use `docker stop`.

## Verify

```powershell
npm run typecheck:app
npm run test:app
npm run build:app
npm run build:windows
node portdeck-app/dist/cli.js status --json
```

Manual UI checks should cover:

1. The tray icon opens and hides the dashboard.
2. A local HTTP listener appears with its port and opens in the default browser.
3. Docker services appear when Docker Desktop is running.
4. Folder opens only when safe path attribution exists.
5. Stop requires confirmation and refreshes the snapshot afterward.
6. Missing Docker, inaccessible process metadata, and discovery warnings do not crash the app.
7. The header cannot drag the flyout away from its taskbar edge, and every header control has a readable text label.
8. Settings switches between Dark, Light, and System and displays the current preview version and source commit.
