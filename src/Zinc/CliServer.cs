using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Zinc.Cli;
using Zinc.Terminal;

namespace Zinc;

/// <summary>
/// Where zn's requests arrive: a named pipe only your Windows account can open (and never over the network), one
/// request per connection. The pipe side only reads, waits and writes; the work runs on the UI thread
/// (<see cref="App"/>'s handler).
/// </summary>
internal static class CliServer
{
    private static int s_lastTerminalId;
    private static CancellationTokenSource? s_stop;

    /// <summary>This Zinc's pipe (every terminal's ZINC_PIPE); null until it listens.</summary>
    public static string? PipeName { get; private set; }

    /// <summary>zn's folder (bin\ next to zinc.exe), if this copy has one.</summary>
    public static string? ZnFolder { get; } = Path.Combine(AppContext.BaseDirectory, "bin") is var bin && File.Exists(Path.Combine(bin, "zn.exe"))
        ? bin : null;

    /// <summary>A new terminal's id; its shell will start with it and the pipe's name in its environment.</summary>
    public static int NewTerminal(TerminalControl control)
    {
        var id = Interlocked.Increment(ref s_lastTerminalId);
        control.LaunchEnvironment = environment =>
        {
            if (PipeName is { } pipe)
            {
                CliEnvironment.Add(environment, id, pipe, Environment.GetEnvironmentVariable, ZnFolder);
            }
        };
        return id;
    }

    /// <summary>Listens on <paramref name="name"/>; each request goes to <paramref name="handle"/>, whose task gives the reply.</summary>
    public static void Start(string name, Func<CliRequest, Task<CliReply>> handle)
    {
        PipeName = name;
        s_stop = new CancellationTokenSource();
        var stop = s_stop.Token;
        var thread = new Thread(() => Listen(name, handle, stop)) { IsBackground = true, Name = "zn pipe" };
        thread.Start();
        Log.Write($"cli: listening on {name}");
    }

    public static void Stop() => s_stop?.Cancel();

    private static void Listen(string name, Func<CliRequest, Task<CliReply>> handle, CancellationToken stop)
    {
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, Security());
                pipe.WaitForConnectionAsync(stop).GetAwaiter().GetResult();
                var connected = pipe;
                pipe = null;
                _ = Task.Run(() => Serve(connected, handle));
                failures = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // E.g. the name taken by another program: try again, slower, and give up after a while.
                Log.Write($"cli: pipe trouble ({ex.Message})");
                if (++failures >= 10)
                {
                    Log.Write("cli: giving up; zn can't reach this Zinc");
                    break;
                }

                stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(failures));
            }
            finally
            {
                pipe?.Dispose();
            }
        }
    }

    /// <summary>Your account may connect; connections from the network may not (a pipe is reachable over SMB otherwise).</summary>
    private static PipeSecurity Security()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User!;
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl,
            AccessControlType.Deny));
        return security;
    }

    private static async Task Serve(NamedPipeServerStream pipe, Func<CliRequest, Task<CliReply>> handle)
    {
        using (pipe)
        {
            try
            {
                var utf8 = new UTF8Encoding(false);
                using var reader = new StreamReader(pipe, utf8, false, leaveOpen: true);
                using var writer = new StreamWriter(pipe, utf8, leaveOpen: true) { NewLine = "\n" };
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var line = await ReadLine(reader, timeout.Token);
                var reply = line == null ? null
                    : CliProtocol.ReadRequest(line) is { } request ? await handle(request)
                    : CliReply.Fail(CliCodes.Usage, "Zinc couldn't read that request: is zn newer than Zinc?");
                if (reply != null)
                {
                    await writer.WriteLineAsync(CliProtocol.Write(reply));
                    await writer.FlushAsync();
                    pipe.WaitForPipeDrain();
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // zn went away (Ctrl+C, its terminal closed by its own command) or never sent a whole line.
            }
            catch (Exception ex)
            {
                Log.Crash("zn request", ex);
            }
        }
    }

    /// <summary>One line, at most <see cref="CliProtocol.MaxLine"/> characters; null if the other side stops first.</summary>
    private static async Task<string?> ReadLine(StreamReader reader, CancellationToken cancel)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancel);
            if (read == 0)
            {
                return null;
            }

            var end = Array.IndexOf(buffer, '\n', 0, read);
            sb.Append(buffer, 0, end < 0 ? read : end);
            if (end >= 0)
            {
                return sb.ToString();
            }

            if (sb.Length > CliProtocol.MaxLine)
            {
                return null;
            }
        }
    }
}
