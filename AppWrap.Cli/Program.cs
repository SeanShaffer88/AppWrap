using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AppWrap.Cli;

/// <summary>
/// The CLI's only console surface: parse, route, print. All argument parsing
/// lives in CliArguments and all command logic in CommandRouter, so both are
/// exercised by the checks without a console.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        CliInvocation invocation;
        try { invocation = CliArguments.Parse(args); }
        catch (CliUsageException ex)
        {
            if (args.Contains("--json", StringComparer.Ordinal))
            {
                var envelope = new JsonObject
                {
                    ["ok"] = false,
                    ["command"] = ex.Command ?? "",
                    ["exitCode"] = 2,
                    ["result"] = null,
                    ["diagnostics"] = new JsonArray(),
                    ["error"] = ex.Message,
                };
                Console.WriteLine(envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                foreach (var line in CliHelp.For(ex.Command ?? "")) Console.WriteLine(line);
            }
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var environment = CliEnvironment.Default(version) with
        {
            Verbose = invocation.Verbose ? message => Console.Error.WriteLine(message) : null,
        };
        var result = await CommandRouter.ExecuteAsync(invocation, environment, cancellation.Token);
        if (invocation.Json) Console.WriteLine(result.ToJson());
        else
        {
            foreach (var line in result.Lines) Console.WriteLine(line);
            if (result.Error is not null) Console.Error.WriteLine("Error: " + result.Error);
        }
        return result.ExitCode;
    }
}
