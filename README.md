# logz

Dead-simple cross-language TCP log sink for debugging distributed systems.

This is a **reference implementation** — the idea matters more than the code. The protocol is intentionally simple so you (or an AI coding assistant) can write a client in any language in minutes. The clients here are examples; adapt or rewrite them however you like.

Drop a single file into your project, run the server, see logs from all your services in one place. No dependencies, no setup, no packages to install.

**This is a development-time tool.** It's designed for debugging on your local machine or dev environment — not for production logging. There's no authentication, no TLS, no guaranteed delivery. For production, use proper observability tools like Grafana Loki, OpenTelemetry, or ELK.

## Why

When debugging across multiple processes or languages (Go service + .NET client + TypeScript UI), you need one place to see what's happening. `logz` is a TCP server that collects log lines from any language and writes them to a file and console. Clients are single files with zero dependencies — copy one into your project and start logging.

## Quick start

### 1. Run the server

```bash
# Requires .NET 10+ SDK (uses C# file-based execution)
dotnet run server/logzd.cs
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

### UDP (no client needed)

The server doesn't support UDP yet, but the protocol is simple enough that you don't even need a client library. For single-line messages, just send a UDP packet — no `__END__` framing required since each packet is one message:

```bash
# Bash — no dependencies at all
echo "$(date +%H:%M:%S) $(hostname) MY-SRC some debug message" | nc -u -w0 127.0.0.1 12345
```

```python
# Python — stdlib only
import socket, time, platform
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.sendto(f"{time.strftime('%H:%M:%S')} {platform.node()} MY-SRC hello from python".encode(), ("127.0.0.1", 12345))
```

```powershell
# PowerShell
$b = [Text.Encoding]::UTF8.GetBytes("$(Get-Date -f HH:mm:ss) $env:COMPUTERNAME MY-SRC hello from powershell")
(New-Object Net.Sockets.UdpClient).Send($b, $b.Length, "127.0.0.1", 12345)
```

This is the "just get a message out" escape hatch — works from any language, any environment, zero setup. Adding UDP support to the server is a straightforward extension left as an exercise (or ask your AI assistant to do it).

## Usage with Claude Code

Add this to your project's `CLAUDE.md` so Claude knows how to use logz for debugging:

```markdown
## Debugging with logz
Drop-in TCP log sink for cross-process debugging. Repo: https://github.com/mtmk/logz
- Server: clone the repo and run `dotnet run server/logzd.cs` (listens on port 12345, writes to logz.log)
- .NET: Copy `clients/dotnet/Logz.cs` into project, use `Logz.Log("SRC", "message")`
- Go: Copy `clients/go/logz.go` into project, use `logz.Log("SRC", "message")`
- TypeScript: Copy `clients/typescript/logz.ts`, use `logz("SRC", "message")`
- Logs collected in logz.log — read this file to check trace output
- Edit defaults at top of client file if env vars aren't available
```

Claude Code can then:
- Copy the right client file into your project when you need trace logging
- Add `Logz.Log` calls to instrument code paths you're debugging
- Read `logz.log` to analyze what happened
- Use `Logz.WatchAsync` to find hangs and `Logz.InstallCrashHandler` to catch crashes

No MCP server or special integration needed — it's just a file and a log.

## Extras

The `extras/` folder has additional tools for capturing traffic from browsers and HTTP proxies:

### mitmproxy addon (`extras/mitmproxy_log_addon.py`)

Logs all HTTP requests and responses (method, URL, headers, body) to a file. Use with [mitmproxy](https://mitmproxy.org/) to capture traffic from any application:

```bash
# Install mitmproxy: https://docs.mitmproxy.org/stable/overview-installation/
mitmproxy -s extras/mitmproxy_log_addon.py
```

Configure your app to use `http://localhost:8080` as its proxy. All traffic is logged to `mitmproxy-requests.log` (configurable via `LOGZ_MITMPROXY_FILE` env var).

### Browser CDP logger (`extras/blogz.js`)

Launches a Chromium-based browser with remote debugging enabled and captures network traffic, console output, and page events via the [Chrome DevTools Protocol](https://chromedevtools.github.io/devtools-protocol/). Useful for debugging web apps where you need to see exactly what the browser is doing:

```bash
cd extras && npm install && node blogz.js 1
# Launches browser instance 1 on debug port 9221
```

Set `LOGZ_CHROMIUM_PATH` to your browser executable (Chrome, Edge, Brave, etc.).

## Design choices

- **Single-file clients** — no package managers, no transitive dependencies. Copy and use.
- **Fire-and-forget** — logging never blocks or crashes your app. If logzd isn't running, messages are silently dropped.
- **Background send** — .NET and Go clients queue messages and send on a background thread. TypeScript uses async drain.
- **netstandard2.0 compatible** — the .NET client works everywhere from .NET Framework 4.6.1 to .NET 9.
