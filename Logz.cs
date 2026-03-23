#pragma warning disable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public static class Logz
{
    private static readonly string ServerHost =
        Environment.GetEnvironmentVariable("LOGZ_HOST") ?? "127.0.0.1";
    private static readonly int ServerPort =
        int.TryParse(Environment.GetEnvironmentVariable("LOGZ_PORT"), out var p) ? p : 12345;
    private static readonly string Hostname = Dns.GetHostName();

    private static readonly BlockingCollection<(string Src, string Message)> LogQueue =
        new BlockingCollection<(string, string)>(1000);

    private static readonly AsyncLocal<string> _scopeId = new AsyncLocal<string>();

    static Logz()
    {
        var thread = new Thread(BackgroundSender) { IsBackground = true, Name = "Logz" };
        thread.Start();
    }

    /// <summary>
    /// Send a log message. If inside a scope, the scope ID is prepended to src automatically.
    /// </summary>
    public static void Log(string src, string message)
    {
        var scope = _scopeId.Value;
        var fullSrc = scope != null ? $"{src}:{scope}" : src;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var srcLine = $"{timestamp} {Hostname} {fullSrc}";
        LogQueue.TryAdd((srcLine, message));
    }

    /// <summary>
    /// Begin a named scope. All Logz.Log calls on this async flow will include the scope ID.
    /// Dispose the returned value to end the scope.
    /// </summary>
    public static IDisposable BeginScope(string scopeId)
    {
        var previous = _scopeId.Value;
        _scopeId.Value = scopeId;
        Log("SCOPE", $"begin {scopeId}");
        return new ScopeDisposable(previous);
    }

    /// <summary>
    /// Install a global handler that logs unhandled exceptions and unobserved task exceptions
    /// to logz before the process crashes.
    /// </summary>
    public static void InstallCrashHandler()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log("CRASH", $"Unhandled exception (terminating={e.IsTerminating}):\n{e.ExceptionObject}");
            Thread.Sleep(500); // give the background sender time to flush
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("CRASH", $"Unobserved task exception:\n{e.Exception}");
        };
    }

    /// <summary>
    /// Run an async operation with watchdog logging. Logs start, warns if slow, logs completion.
    /// </summary>
    public static async Task WatchAsync(string name, Func<Task> action, TimeSpan? warnAfter = null)
    {
        var threshold = warnAfter ?? TimeSpan.FromSeconds(5);
        Log("WATCH", $"{name}: started");
        var sw = Stopwatch.StartNew();

        using var warnCts = new CancellationTokenSource();
        var warnTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(threshold, warnCts.Token);
                Log("WATCH", $"{name}: SLOW ({sw.Elapsed.TotalSeconds:F1}s, still running)");
                // Keep warning every 5s
                while (!warnCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), warnCts.Token);
                    Log("WATCH", $"{name}: STILL RUNNING ({sw.Elapsed.TotalSeconds:F1}s)");
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        try
        {
            await action();
            sw.Stop();
            Log("WATCH", $"{name}: completed ({sw.Elapsed.TotalSeconds:F1}s)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log("WATCH", $"{name}: FAILED ({sw.Elapsed.TotalSeconds:F1}s) {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            warnCts.Cancel();
            try { await warnTask; }
            catch { }
        }
    }

    /// <summary>
    /// Run an async operation returning a value with watchdog logging.
    /// </summary>
    public static async Task<T> WatchAsync<T>(string name, Func<Task<T>> action, TimeSpan? warnAfter = null)
    {
        T result = default;
        await WatchAsync(name, async () => { result = await action(); }, warnAfter);
        return result;
    }

    private sealed class ScopeDisposable : IDisposable
    {
        private readonly string _previous;

        public ScopeDisposable(string previous) => _previous = previous;

        public void Dispose()
        {
            var ending = _scopeId.Value;
            _scopeId.Value = _previous;
            Log("SCOPE", $"end {ending}");
        }
    }

    private static void BackgroundSender()
    {
        TcpClient client = null;
        StreamWriter writer = null;

        while (true)
        {
            try
            {
                if (client == null || !client.Connected)
                {
                    client?.Dispose();
                    client = new TcpClient();
                    client.Connect(ServerHost, ServerPort);
                    writer = new StreamWriter(client.GetStream(), Encoding.UTF8) { AutoFlush = true };
                }

                foreach (var item in LogQueue.GetConsumingEnumerable())
                {
                    if (writer == null || client == null || !client.Connected)
                    {
                        LogQueue.TryAdd(item);
                        break;
                    }

                    try
                    {
                        SendMessage(writer, item.Src, item.Message);
                    }
                    catch
                    {
                        LogQueue.TryAdd(item);
                        client?.Dispose();
                        client = null;
                        writer = null;
                        break;
                    }
                }
            }
            catch
            {
                client?.Dispose();
                client = null;
                writer = null;
                Thread.Sleep(1000);
            }
        }
    }

    private static void SendMessage(StreamWriter writer, string srcLine, string message)
    {
        writer.WriteLine(srcLine);

        var lines = message.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        foreach (var line in lines)
        {
            if (line == "__END__")
                writer.WriteLine("__END____END__");
            else
                writer.WriteLine(line);
        }

        writer.WriteLine("__END__");
    }
}
