using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Zinc.Cli;

/// <summary>zn: reads the command line, sends one request to the running Zinc, prints the answer.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var parsed = CliCommandLine.Parse(args);
        if (parsed.Help is { } help)
        {
            Console.Out.Write(help);
            return CliExit.Done;
        }

        if (parsed.Request is not { } request)
        {
            Console.Error.WriteLine(parsed.Error);
            return CliExit.Usage;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable(CliPipe.TerminalVariable), out var caller))
        {
            request.Caller = caller;
        }

        if (request.Cwd is { } cwd)
        {
            // Relative to where zn runs (from WSL, a \\wsl.localhost\… path: Zinc maps it back).
            request.Cwd = Path.GetFullPath(cwd);
        }

        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        if (Send(request) is not { } reply)
        {
            if (request.Cmd == "version")
            {
                Console.Out.WriteLine($"zn {version}");
            }

            Console.Error.WriteLine("Zinc isn't running.");
            return CliExit.NotRunning;
        }

        if (!reply.Ok)
        {
            Console.Error.WriteLine(reply.Error ?? "Zinc couldn't do that.");
            return CliProtocol.ExitCodeFor(reply);
        }

        if (reply.Target is { } target)
        {
            Console.Error.WriteLine($"({target})");
        }

        switch (request.Cmd)
        {
            case "version" when parsed.Json:
                Console.Out.WriteLine(JsonSerializer.Serialize(new CliReply { Ok = true, Version = reply.Version },
                    CliJsonIndented.Default.CliReply));
                break;
            case "version":
                Console.Out.WriteLine($"zn {version}");
                Console.Out.WriteLine($"Zinc {reply.Version}");
                break;
            case "list" or "window list" or "workspace list" or "pane list" or "terminal list" when reply.Tree is { } tree:
                var shown = CliText.Filter(tree, request);
                Console.Out.Write(parsed.Json
                    ? JsonSerializer.Serialize(shown, CliJsonIndented.Default.CliTree) + Environment.NewLine
                    : CliText.List(shown, request.Cmd.Split(' ')[0] is var level && level != "list" ? level : ""));
                break;
            default:
                if (reply.Id is { } id)
                {
                    Console.Out.WriteLine(id);
                }

                break;
        }

        return CliExit.Done;
    }

    /// <summary>The request's answer; null when no Zinc answers.</summary>
    private static CliReply? Send(CliRequest request)
    {
        if (CliPipe.Find() is not { } name)
        {
            return null;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            var utf8 = new UTF8Encoding(false);
            using var writer = new StreamWriter(pipe, utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            using var reader = new StreamReader(pipe, utf8, false, leaveOpen: true);
            writer.WriteLine(CliProtocol.Write(request));
            // No time limit: a close may wait on your answer to "a command is still running".
            return reader.ReadLine() is { } line ? CliProtocol.ReadReply(line) ?? Garbled() : null;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static CliReply Garbled() => CliReply.Fail(CliCodes.Failed, "Zinc's answer couldn't be read: is zn older than Zinc?");
}
