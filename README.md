# logz

A TCP log sink for debugging several processes at once on a development
machine. Each client is a single file with no dependencies. Copy the one
for your language into the project, run the server, and log lines from
every process end up in one console and one file.

This is a development tool. There is no authentication, no TLS and no
delivery guarantee. For production logging use something like
OpenTelemetry, Grafana Loki or ELK.

The protocol is small on purpose, so writing a client for another
language takes a few minutes. The clients here are examples to copy and
change.

## Why

When a bug spans a Go service, a .NET client and a TypeScript UI, the
logs are in three places with three formats. logz gives them one place:
a TCP server that collects lines from any language, prints them with
colour, and appends them to a file.

## Quick start

### 1. Run the server

```bash
# Requires the .NET 10 SDK (file-based execution)
dotnet run server/logzd.cs
```

The server listens on port `12345`, prints to the console, and appends to
`logz.log` (or `LOGZ_FILE`). Type a keyword to show only matching lines,
press Enter to clear the filter.

### 2. Copy a client into your project

| Language | File | Usage |
|----------|------|-------|
| C# / .NET | `clients/dotnet/Logz.cs` | `Logz.Log("SRC", "message")` |
| Go | `clients/go/logz.go` | `logz.Log("SRC", "message")` |
| Go, minimal | `clients/go-simple/logz.go` | `logz.Log("SRC", "message")` |
| TypeScript (Node) | `clients/typescript/logz.ts` | `logz("SRC", "message")` |
| TypeScript (Deno) | `clients/deno/logz.ts` | `await logz("SRC", "message")` |

The Go client queues messages and sends them from a background
goroutine. The minimal one opens a connection per message and prints its
own progress to stdout, which helps when you are not sure the messages
are getting out at all.

### 3. Watch the logs

```bash
tail -f logz.log
tail -f logz.log | grep "MY-SRC"
tail -f logz.log | grep -E "SRC-A|SRC-B"
```

## Configuration

All clients default to `127.0.0.1:12345`. Override with environment
variables, or edit the defaults at the top of the client file where
environment variables are not available (containers, embedded hosts).

| Variable | Description | Default |
|----------|-------------|---------|
| `LOGZ_HOST` | Server host or IP | `127.0.0.1` |
| `LOGZ_PORT` | Server port | `12345` |
| `LOGZ_ADDR` | Go clients: `host:port` | `127.0.0.1:12345` |
| `LOGZ_FILE` | Server: log file path | `logz.log` |

To log from somewhere else, such as a Kubernetes pod, to a development
machine, set `LOGZ_HOST` to that machine's address.

## .NET client extras

Correlation scopes tag every log line within an async flow:

```csharp
using var _ = Logz.BeginScope("test-auth-flow");
Logz.Log("SVC", "starting");  // logged as SVC:test-auth-flow
```

`WatchAsync` warns when an operation is slow and logs when it finishes:

```csharp
await Logz.WatchAsync("db-query", () => db.QueryAsync(...), warnAfter: TimeSpan.FromSeconds(3));
// db-query: started
// db-query: SLOW (3.2s, still running)
// db-query: completed (4.1s)
```

`InstallCrashHandler` logs unhandled exceptions before the process exits:

```csharp
Logz.InstallCrashHandler();
```

`LogzLogger.cs` routes `Microsoft.Extensions.Logging` to logz:

```csharp
builder.Logging.AddLogz();
```

## Protocol

Clients send each message over TCP as:

```
<timestamp> <hostname> <source>\n
<message line 1>\n
<message line 2>\n
__END__\n
```

A message line containing `__END__` is escaped as `__END____END__`.

### UDP

The server does not accept UDP yet. If it did, a single-line message
would need no client at all, since each packet is one message:

```bash
echo "$(date +%H:%M:%S) $(hostname) MY-SRC some debug message" | nc -u -w0 127.0.0.1 12345
```

```python
import socket, time, platform
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.sendto(f"{time.strftime('%H:%M:%S')} {platform.node()} MY-SRC hello".encode(), ("127.0.0.1", 12345))
```

```powershell
$b = [Text.Encoding]::UTF8.GetBytes("$(Get-Date -f HH:mm:ss) $env:COMPUTERNAME MY-SRC hello")
(New-Object Net.Sockets.UdpClient).Send($b, $b.Length, "127.0.0.1", 12345)
```

## Extras

`extras/` has two tools for capturing traffic that is not your own code's
logging.

### mitmproxy addon

`extras/mitmproxy_log_addon.py` records HTTP traffic through
[mitmproxy](https://mitmproxy.org/) as a JSONL index plus one file per
body:

```bash
mitmproxy -s extras/mitmproxy_log_addon.py
```

Point the application at `http://localhost:8080` as its proxy.

Each run gets its own folder under `LOGZ_MITMPROXY_DIR` (default
`mitmproxy-logs/`), created on the first event:

```
<LOGZ_MITMPROXY_DIR>/
  latest.txt              name of the newest run folder
  20260729T131502123/
    index.log             one JSON object per line
    bodies/<id>.req|.res  request and response bodies
```

Each `index.log` line has `event` set to `request`, `response` or
`error`, correlated by flow `id`. Response and error lines repeat the
method and URL, so every line stands alone. Requests carry the client
address (`client`) and `http_version`. Responses add `content_type`,
`server_ip`, `ttfb_ms` (time to first response byte) and `elapsed_ms`.

Bodies are never inline. They go to `bodies/` and the index points at
them with `body_file`, so index lines stay small and `grep` and `jq`
stay fast. Body files are capped at 1 MiB (`LOGZ_MITMPROXY_MAX_BODY`);
a capped body is marked `"truncated": true` with its full size in
`body_bytes`. Bodies containing NUL bytes are marked `"binary": true`.
Streamed bodies mitmproxy did not capture get a `body_note` instead of a
file.

From inside a run folder, or after `cd "$(cat latest.txt)"`:

```bash
jq -c 'select(.id=="<flow-id>")' index.log
jq -r 'select(.event=="response" and .elapsed_ms>2000) | .url' index.log
grep -rl "some string" bodies/
```

The recorded traffic includes whatever went over the wire, cookies and
tokens included. Keep run folders out of version control.

### Browser CDP logger

`extras/blogz.js` launches a Chromium-based browser with remote
debugging enabled and records network traffic, console output and page
events through the [Chrome DevTools
Protocol](https://chromedevtools.github.io/devtools-protocol/):

```bash
cd extras && npm install && node blogz.js 1
# browser instance 1, debug port 9221
```

Set `LOGZ_CHROMIUM_PATH` to the browser executable (Chrome, Edge, Brave
and so on).

## Design choices

* Single-file clients. No package manager and no transitive
  dependencies.
* Fire and forget. Logging never blocks or crashes the application. If
  the server is not running, messages are dropped.
* Background sending. The .NET and Go clients queue messages and send
  them from a background thread; the TypeScript clients drain
  asynchronously.
* The .NET client targets netstandard2.0, so it runs on .NET Framework
  4.6.1 and later as well as modern .NET.

## License

MIT
