# logz

Dead-simple cross-language TCP log sink for debugging distributed systems.

Drop a single file into your project, run the server, see logs from all your services in one place. No dependencies, no setup, no packages to install.

## Why

When debugging across multiple processes or languages (Go service + .NET client + TypeScript UI), you need one place to see what's happening. `logz` is a TCP server that collects log lines from any language and writes them to a file and console. Clients are single files with zero dependencies — copy one into your project and start logging.

## Quick start

### 1. Run the server

```bash
# Requires .NET 8+ SDK
dotnet run --project server/logzd.cs
```

The server listens on port `12345`, prints logs to console with color coding, and appends to `logz.log` (configurable via `LOGZ_FILE` env var). Type a filter keyword to show only matching lines, press Enter to clear.

### 2. Drop a client into your project

Copy the single file for your language:

| Language | File | Usage |
|----------|------|-------|
| C# / .NET | `clients/dotnet/Logz.cs` | `Logz.Log("SRC", "message")` |
| Go | `clients/go/logz.go` | `logz.Log("SRC", "message")` |
| TypeScript (Node) | `clients/typescript/logz.ts` | `logz("SRC", "message")` |
| TypeScript (Deno) | `clients/deno/logz.ts` | `await logz("SRC", "message")` |

That's it. No packages, no config files.

### 3. Watch logs

```bash
# Real-time tail
tail -f logz.log

# Filter to specific source
tail -f logz.log | grep "MY-SRC"

# Multiple filters
tail -f logz.log | grep -E "SRC-A|SRC-B"
```

## Configuration

All clients default to `127.0.0.1:12345`. Override with environment variables:

| Variable | Description | Default |
|----------|-------------|---------|
| `LOGZ_HOST` | Server hostname/IP | `127.0.0.1` |
| `LOGZ_PORT` | Server port | `12345` |
| `LOGZ_ADDR` | Go clients: `host:port` combined | `127.0.0.1:12345` |
| `LOGZ_FILE` | Server: log file path | `logz.log` |

For remote servers (e.g. K8s pods logging to a dev machine), set `LOGZ_HOST` to the host's IP.

## .NET client extras

The .NET client (`Logz.cs`) includes additional helpers beyond basic logging:

**Correlation scopes** — tag all logs within an async flow:
```csharp
using var _ = Logz.BeginScope("test-auth-flow");
Logz.Log("SVC", "starting");  // logged as SVC:test-auth-flow
```

**Async watchdog** — warns if an operation is slow, logs completion:
```csharp
await Logz.WatchAsync("db-query", () => db.QueryAsync(...), warnAfter: TimeSpan.FromSeconds(3));
// Logs: "db-query: started"
// Logs: "db-query: SLOW (3.2s, still running)"  -- if exceeds threshold
// Logs: "db-query: completed (4.1s)"
```

**Crash handler** — captures unhandled exceptions before process dies:
```csharp
Logz.InstallCrashHandler();
```

**ILogger bridge** — route ASP.NET / Microsoft.Extensions.Logging to logz:
```csharp
builder.Logging.AddLogz();  // using LogzLogger.cs
```

## Protocol

Clients send messages over TCP:

```
<timestamp> <hostname> <source>\n
<message line 1>\n
<message line 2>\n
__END__\n
```

If a message line contains `__END__`, it's escaped as `__END____END__`.

## Design choices

- **Single-file clients** — no package managers, no transitive dependencies. Copy and use.
- **Fire-and-forget** — logging never blocks or crashes your app. If logzd isn't running, messages are silently dropped.
- **Background send** — .NET and Go clients queue messages and send on a background thread. TypeScript uses async drain.
- **netstandard2.0 compatible** — the .NET client works everywhere from .NET Framework 4.6.1 to .NET 9.
