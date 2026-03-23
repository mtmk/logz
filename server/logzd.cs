using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

var colors = new Dictionary<string, (ConsoleColor Foreground, ConsoleColor Background)>();
var colors2 = new Dictionary<string, (ConsoleColor Foreground, ConsoleColor Background)>();

var filter = string.Empty;

// Channel for serializing console/file writes
var writeChannel = Channel.CreateBounded<(string Src, string Src2, string Message)>(
    new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });

// Command input task
_ = Task.Run(() =>
{
    while (true)
    {
        var cmd = Console.ReadLine();
        if (cmd == "exit")
        {
            Console.WriteLine("Exiting...");
            Environment.Exit(0);
        }
        else if (string.IsNullOrWhiteSpace(cmd) || cmd == "clear")
        {
            filter = string.Empty;
            Console.WriteLine("[FILTER] Cleared");
        }
        else
        {
            filter = cmd;
            Console.WriteLine($"[FILTER] Set to: {filter}");
        }
    }
});

// Writer task - serializes all console/file output
_ = Task.Run(async () =>
{
    await foreach (var (src, src2, message) in writeChannel.Reader.ReadAllAsync())
    {
        // Apply filter
        if (!string.IsNullOrEmpty(filter) &&
            !src.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
            !src2.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var fg = Console.ForegroundColor;
        var bg = Console.BackgroundColor;

        if (colors.TryGetValue(src, out var color))
        {
            Console.ForegroundColor = color.Foreground;
            Console.BackgroundColor = color.Background;
        }

        if (colors2.TryGetValue(src2, out var color2))
        {
            Console.ForegroundColor = color2.Foreground;
            Console.BackgroundColor = color2.Background;
        }

        Console.Write(message);

        Console.ForegroundColor = fg;
        Console.BackgroundColor = bg;

        Console.Out.Flush();
    }
});

// ── Edit these defaults or set LOGZ_PORT / LOGZ_FILE env vars ──
const int DefaultPort = 12345;
const string DefaultLogFile = "logz.log";
// ────────────────────────────────────────────────────────────────

var PORT = int.TryParse(Environment.GetEnvironmentVariable("LOGZ_PORT"), out var ep) ? ep : DefaultPort;
var listener = new TcpListener(IPAddress.Any, PORT);
listener.Start();

Console.WriteLine($"TCP server listening on port {PORT}...");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = HandleClientAsync(client, writeChannel.Writer);
}

async Task HandleClientAsync(TcpClient client, ChannelWriter<(string, string, string)> writer)
{
    // Per-client timeout: if a client doesn't send a complete message within 10 seconds, drop it
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    try
    {
        client.ReceiveTimeout = 10_000;
        client.SendTimeout = 5_000;

        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            while (!cts.Token.IsCancellationRequested)
            {
                // First line is source
                var srcLine = await reader.ReadLineAsync(cts.Token);
                if (srcLine == null) break;

                // Read message lines until __END__
                var messageLines = new List<string>();
                while (true)
                {
                    var line = await reader.ReadLineAsync(cts.Token);
                    if (line == null) break;

                    if (line == "__END__")
                    {
                        break;
                    }
                    else if (line == "__END____END__")
                    {
                        messageLines.Add("__END__");
                    }
                    else
                    {
                        messageLines.Add(line);
                    }
                }

                var message = string.Join("\n", messageLines);
                var fullMessage = $"{srcLine} {message}\n";

                AppendToLogFile(fullMessage);

                var ts = Regex.Split(srcLine, @"\s+");
                var src = ts.Length > 2 ? ts[2] : string.Empty;
                var src2 = ts.Length > 3 ? ts[3] : string.Empty;

                await writer.WriteAsync((src, src2, fullMessage), cts.Token);
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Client timed out - silently drop
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Client error: {ex.Message}");
    }
}

var logFile = Environment.GetEnvironmentVariable("LOGZ_FILE") ?? DefaultLogFile;

void AppendToLogFile(string message)
{
    try
    {
        using var stream = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.Write(message);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to write to log file: {ex.Message}");
    }
}
