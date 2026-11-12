import { describe, expect, test } from "vitest";
import { parseWindowsSnapshot } from "./windows-discovery.js";

describe("parseWindowsSnapshot", () => {
  test("normalizes listening connections, process metadata, and activity", () => {
    const result = parseWindowsSnapshot({
      connections: [
        { localAddress: "0.0.0.0", localPort: 3000, owningProcess: 42, state: "Listen" },
        { localAddress: "0:0:0:0:0:0:0:1", localPort: 3000, owningProcess: 42, state: "Listen" },
        { localAddress: "0.0.0.0", localPort: 3000, owningProcess: 42, state: "Listen" }
      ],
      processes: [{
        processId: 42,
        name: "node.exe",
        commandLine: "node server.js",
        creationDate: "2026-11-12T09:59:00.000Z",
        workingSetBytes: "1048576",
        cpuPercent: 3.5
      }]
    }, new Date("2026-11-12T10:00:00.000Z"));

    expect(result.processPorts).toEqual([
      { pid: 42, processName: "node.exe", port: 3000, protocol: "TCP", address: "0.0.0.0" },
      { pid: 42, processName: "node.exe", port: 3000, protocol: "TCP", address: "::1" }
    ]);
    expect(result.processes.get(42)).toEqual({
      pid: 42,
      processName: "node.exe",
      command: "node server.js",
      uptimeSeconds: 60
    });
    expect(result.processActivityByPid.get(42)).toEqual({ cpuPercent: 3.5, memoryRssBytes: 1_048_576 });
  });

  test("keeps a listener when process metadata is unavailable", () => {
    const result = parseWindowsSnapshot({
      connections: { localAddress: "127.0.0.1", localPort: "8080", owningProcess: "99", state: "Listen" },
      processes: null
    });

    expect(result.processPorts).toEqual([
      { pid: 99, processName: "unknown", port: 8080, protocol: "TCP", address: "127.0.0.1" }
    ]);
    expect(result.processes.get(99)).toEqual({ pid: 99, processName: "unknown" });
  });

  test("ignores malformed rows without failing the snapshot", () => {
    const result = parseWindowsSnapshot({
      connections: [
        { localAddress: "127.0.0.1", localPort: 70000, owningProcess: 1, state: "Listen" },
        { localAddress: "127.0.0.1", localPort: 3000, owningProcess: 0, state: "Listen" }
      ],
      processes: [{ processId: "not-a-pid", name: "bad.exe" }]
    });

    expect(result.processPorts).toEqual([]);
    expect(result.processes.size).toBe(0);
  });

  test("accepts state-less development listeners without treating browser traffic as listeners", () => {
    const result = parseWindowsSnapshot({
      connections: [
        {
          localAddress: "127.0.0.1",
          localPort: 5173,
          owningProcess: 10,
          remoteAddress: "0.0.0.0",
          remotePort: 0,
          state: null
        },
        {
          localAddress: "10.0.0.2",
          localPort: 55000,
          owningProcess: 11,
          remoteAddress: "0.0.0.0",
          remotePort: 0,
          state: null
        }
      ],
      processes: [
        { processId: 10, name: "node.exe", commandLine: "node server.js" },
        { processId: 11, name: "chrome.exe", commandLine: "chrome.exe" }
      ]
    });

    expect(result.processPorts).toEqual([
      { pid: 10, processName: "node.exe", port: 5173, protocol: "TCP", address: "127.0.0.1" }
    ]);
  });
});
