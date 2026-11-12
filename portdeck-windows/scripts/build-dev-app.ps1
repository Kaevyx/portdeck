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
  & $dotnetCommand build portdeck-windows\PortDeck.Windows.csproj
  if ($LASTEXITCODE -ne 0) { throw "Could not build PortDeck for Windows." }
} finally {
  Pop-Location
}
