$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$localDotnet = Join-Path $repositoryRoot ".dotnet\dotnet.exe"
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$sdkVersions = @(& $dotnetCommand --list-sdks 2>$null)
if ($LASTEXITCODE -ne 0 -or $sdkVersions.Count -eq 0) {
  throw "The .NET 10 SDK is required. Install it with: winget install Microsoft.DotNet.SDK.10"
}

Push-Location $repositoryRoot
try {
  npm run build:app
  if ($LASTEXITCODE -ne 0) { throw "Could not build the PortDeck helper." }

  $previousRoot = $env:PORTDECK_REPO_ROOT
  $env:PORTDECK_REPO_ROOT = $repositoryRoot
  try {
    $launchStartedAt = Get-Date
    $previousErrorActionPreference = $ErrorActionPreference
    try {
      $ErrorActionPreference = "Continue"
      $runOutput = @(& $dotnetCommand run --project portdeck-windows\PortDeck.Windows.csproj -- --show 2>&1)
      $runExitCode = $LASTEXITCODE
    } finally {
      $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($runExitCode -ne 0) {
      $codeIntegrityEvent = Get-WinEvent `
        -LogName "Microsoft-Windows-CodeIntegrity/Operational" `
        -MaxEvents 40 `
        -ErrorAction SilentlyContinue |
        Where-Object {
          $_.TimeCreated -ge $launchStartedAt -and
          $_.Message -like "*PortDeck*" -and
          ($_.Id -eq 3033 -or $_.Id -eq 3077)
        } |
        Select-Object -First 1

      if ($null -ne $codeIntegrityEvent) {
        Write-Host @"
Windows Application Control blocked the locally built PortDeck executable because it does not meet this device's enterprise signing requirements.

PortDeck does not bypass Windows security policy. To run this build, either:
  1. Sign PortDeck.exe and PortDeck.dll with an organization-approved code-signing certificate, or
  2. Ask the device administrator for an approved Windows Application Control allow rule for PortDeck development builds, or
  3. Test on a development device that permits unsigned local applications.

Ask your device administrator which signing identity and workflow are approved for local development.
"@ -ForegroundColor Red
        exit 1
      }

      $runOutput | Write-Output
      throw "PortDeck for Windows exited with code $runExitCode."
    }
  } finally {
    $env:PORTDECK_REPO_ROOT = $previousRoot
  }
} finally {
  Pop-Location
}
