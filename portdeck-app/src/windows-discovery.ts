import { stat } from "node:fs/promises";
import path from "node:path";
import { execa } from "execa";
import type { DiscoveredProcessPort, ProcessInfo, ServiceActivity } from "./types.js";

type WindowsConnectionRow = {
  localAddress?: unknown;
  localPort?: unknown;
  owningProcess?: unknown;
  remoteAddress?: unknown;
  remotePort?: unknown;
  state?: unknown;
};

type WindowsProcessRow = {
  processId?: unknown;
  name?: unknown;
  commandLine?: unknown;
  executablePath?: unknown;
  creationDate?: unknown;
  workingSetBytes?: unknown;
  cpuPercent?: unknown;
};

type WindowsSnapshot = {
  connections?: unknown;
  processes?: unknown;
};

export type WindowsLocalDiscovery = {
  processPorts: DiscoveredProcessPort[];
  processes: Map<number, ProcessInfo>;
  processActivityByPid: Map<number, ServiceActivity>;
};

const windowsSnapshotScript = String.raw`
$ErrorActionPreference = 'Stop'
$currentSessionId = (Get-Process -Id $PID).SessionId
$sessionProcesses = @(Get-CimInstance Win32_Process | Where-Object { [int]$_.SessionId -eq $currentSessionId })
$developmentPids = @($sessionProcesses | Where-Object {
  $_.Name -match '^(node|bun|deno|python\d*|py|dotnet|java|ruby|php|go|air|cargo)(\.exe)?$'
} | Select-Object -ExpandProperty ProcessId)
$rawConnections = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue)
if ($developmentPids.Count -gt 0) {
  $rawConnections += @(Get-NetTCPConnection -OwningProcess $developmentPids -ErrorAction SilentlyContinue)
}
$wantedPids = @($rawConnections | Select-Object -ExpandProperty OwningProcess -Unique)
$processRows = @()
$includedPids = @{}
if ($wantedPids.Count -gt 0) {
  $wanted = @{}
  foreach ($processId in $wantedPids) { $wanted[[int]$processId] = $true }
  $performance = @{}
  Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -ErrorAction SilentlyContinue | ForEach-Object {
    $performance[[int]$_.IDProcess] = $_
  }
  $sessionProcesses | Where-Object { $wanted.ContainsKey([int]$_.ProcessId) } | ForEach-Object {
    $created = $null
    if ($_.CreationDate) { $created = $_.CreationDate.ToUniversalTime().ToString('o') }
    $perf = $performance[[int]$_.ProcessId]
    $cpu = $null
    if ($null -ne $perf) { $cpu = [double]$perf.PercentProcessorTime }
    $processRows += [pscustomobject]@{
      processId = $_.ProcessId
      name = $_.Name
      commandLine = $_.CommandLine
      executablePath = $_.ExecutablePath
      creationDate = $created
      workingSetBytes = $_.WorkingSetSize
      cpuPercent = $cpu
    }
  }
  foreach ($processRow in $processRows) { $includedPids[[int]$processRow.processId] = $true }
}
$connections = @($rawConnections | Where-Object {
  $includedPids.ContainsKey([int]$_.OwningProcess)
} | ForEach-Object {
  [pscustomobject]@{
    localAddress = $_.LocalAddress
    localPort = $_.LocalPort
    owningProcess = $_.OwningProcess
    remoteAddress = $_.RemoteAddress
    remotePort = $_.RemotePort
    state = $_.State
  }
})
[pscustomobject]@{ connections = $connections; processes = $processRows } | ConvertTo-Json -Depth 4 -Compress
`.trim();

export async function discoverWindowsLocal(warnings: string[]): Promise<WindowsLocalDiscovery> {
  let stdout: string;
  try {
    const result = await execa("powershell.exe", [
      "-NoLogo",
      "-NoProfile",
      "-NonInteractive",
      "-ExecutionPolicy",
      "Bypass",
      "-Command",
      windowsSnapshotScript
    ], {
      reject: true,
      timeout: 12_000,
      windowsHide: true
    });
    stdout = result.stdout;
  } catch (error) {
    const detail = windowsDiscoveryError(error);
    warnings.push(`Windows process and port discovery is unavailable${detail ? `: ${detail}` : ""}`);
    return emptyWindowsDiscovery();
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(stdout);
  } catch {
    warnings.push("Could not parse Windows process and port discovery output");
    return emptyWindowsDiscovery();
  }

  const discovery = parseWindowsSnapshot(parsed);
  await enrichWindowsWorkingDirectories(discovery.processes);
  return discovery;
}

export function parseWindowsSnapshot(value: unknown, now = new Date()): WindowsLocalDiscovery {
  if (!isRecord(value)) {
    return emptyWindowsDiscovery();
  }

  const snapshot = value as WindowsSnapshot;
  const processRows = normalizeArray(snapshot.processes).filter(isRecord) as WindowsProcessRow[];
  const processByPid = new Map<number, WindowsProcessRow>();
  const processes = new Map<number, ProcessInfo>();
  const processActivityByPid = new Map<number, ServiceActivity>();

  for (const row of processRows) {
    const pid = positiveInteger(row.processId);
    if (pid === undefined) continue;
    processByPid.set(pid, row);

    const command = nonEmptyString(row.commandLine) ?? nonEmptyString(row.executablePath);
    const createdAt = parseDate(row.creationDate);
    processes.set(pid, {
      pid,
      processName: nonEmptyString(row.name) ?? "unknown",
      ...(command ? { command } : {}),
      ...(createdAt ? { uptimeSeconds: Math.max(0, Math.floor((now.getTime() - createdAt.getTime()) / 1000)) } : {})
    });

    const cpuPercent = finiteNumber(row.cpuPercent);
    const memoryRssBytes = nonNegativeInteger(row.workingSetBytes);
    if (cpuPercent !== undefined || memoryRssBytes !== undefined) {
      processActivityByPid.set(pid, {
        ...(cpuPercent !== undefined ? { cpuPercent } : {}),
        ...(memoryRssBytes !== undefined ? { memoryRssBytes } : {})
      });
    }
  }

  const processPorts: DiscoveredProcessPort[] = [];
  const seen = new Set<string>();
  for (const row of normalizeArray(snapshot.connections).filter(isRecord) as WindowsConnectionRow[]) {
    const pid = positiveInteger(row.owningProcess);
    const port = positiveInteger(row.localPort);
    if (pid === undefined || port === undefined || port > 65_535) continue;
    if (!isWindowsListeningConnection(row, processByPid.get(pid))) continue;
    const address = normalizeWindowsAddress(nonEmptyString(row.localAddress) ?? "0.0.0.0");
    const key = `${pid}:${address}:${port}`;
    if (seen.has(key)) continue;
    seen.add(key);
    processPorts.push({
      pid,
      processName: nonEmptyString(processByPid.get(pid)?.name) ?? "unknown",
      port,
      protocol: "TCP",
      address
    });
    if (!processes.has(pid)) {
      processes.set(pid, { pid, processName: "unknown" });
    }
  }

  return { processPorts, processes, processActivityByPid };
}

function isWindowsListeningConnection(row: WindowsConnectionRow, process: WindowsProcessRow | undefined): boolean {
  const state = nonEmptyString(row.state)?.toLowerCase();
  if (state === "listen" || state === "listening") return true;
  if (state !== undefined) return false;

  const remoteAddress = nonEmptyString(row.remoteAddress);
  const remotePort = nonNegativeInteger(row.remotePort);
  const processName = nonEmptyString(process?.name) ?? "";
  return remotePort === 0 &&
    (remoteAddress === "0.0.0.0" || remoteAddress === "::") &&
    /^(?:node|bun|deno|python\d*|py|dotnet|java|ruby|php|go|air|cargo)(?:\.exe)?$/i.test(processName);
}

async function enrichWindowsWorkingDirectories(processes: Map<number, ProcessInfo>): Promise<void> {
  await Promise.all(Array.from(processes.values(), async (process) => {
    if (!process.command) return;
    const cwd = await inferProjectDirectoryFromCommand(process.command);
    if (cwd) process.cwd = cwd;
  }));
}

async function inferProjectDirectoryFromCommand(command: string): Promise<string | undefined> {
  const tokens = Array.from(command.matchAll(/"([^"]+)"|'([^']+)'|([^\s]+)/g), (match) => match[1] ?? match[2] ?? match[3]);
  for (const token of tokens.slice(1)) {
    const candidate = token.replace(/^--?(?:file|script|project|config)=/i, "");
    if (!path.win32.isAbsolute(candidate) || !/\.(?:[cm]?[jt]sx?|py|rb|php|go|dll|jar)$/i.test(candidate)) continue;
    try {
      const details = await stat(candidate);
      if (details.isFile()) return path.dirname(candidate);
    } catch {
      // Commands can contain paths that have disappeared between snapshots.
    }
  }
  return undefined;
}

function emptyWindowsDiscovery(): WindowsLocalDiscovery {
  return {
    processPorts: [],
    processes: new Map(),
    processActivityByPid: new Map()
  };
}

function normalizeArray(value: unknown): unknown[] {
  if (Array.isArray(value)) return value;
  return value === undefined || value === null ? [] : [value];
}

function normalizeWindowsAddress(value: string): string {
  if (value === "0:0:0:0:0:0:0:0") return "::";
  if (value === "0:0:0:0:0:0:0:1") return "::1";
  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

function nonEmptyString(value: unknown): string | undefined {
  if (typeof value !== "string") return undefined;
  const trimmed = value.trim();
  return trimmed || undefined;
}

function finiteNumber(value: unknown): number | undefined {
  if (value === null || value === undefined || value === "") return undefined;
  const number = Number(value);
  return Number.isFinite(number) && number >= 0 ? number : undefined;
}

function positiveInteger(value: unknown): number | undefined {
  const number = finiteNumber(value);
  return number !== undefined && Number.isInteger(number) && number > 0 ? number : undefined;
}

function nonNegativeInteger(value: unknown): number | undefined {
  const number = finiteNumber(value);
  return number !== undefined && Number.isInteger(number) ? number : undefined;
}

function parseDate(value: unknown): Date | undefined {
  const text = nonEmptyString(value);
  if (!text) return undefined;
  const date = new Date(text);
  return Number.isNaN(date.getTime()) ? undefined : date;
}

function windowsDiscoveryError(error: unknown): string | undefined {
  if (!isRecord(error)) return undefined;
  const stderr = nonEmptyString(error.stderr);
  const message = stderr ?? nonEmptyString(error.shortMessage);
  if (!message) return undefined;
  return message.replace(/\s+/g, " ").slice(0, 240);
}
