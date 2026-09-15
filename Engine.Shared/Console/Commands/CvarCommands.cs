using System;
using System.Linq;
using Engine.Shared.Configuration;
using Engine.Shared.IoC;

namespace Engine.Shared.Console.Commands;

public sealed class CvarCommand : IConsoleCommand
{
    public string Name => "cvar";
    public string Description => "Gets or sets a cvar: cvar <name> [value] - use \"> cvar ...\" to force it onto the server";

    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public CvarCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError("Usage: cvar <name> [value]");
            return;
        }

        if (args.Length == 1)
        {
            if (!_cfg.TryGetByName(args[0], out var value))
            {
                shell.WriteError($"Unknown cvar: '{args[0]}'");
                return;
            }

            shell.WriteLine($"{args[0]} = {value}");
            return;
        }

        if (!_cfg.TrySetByName(args[0], args[1], out var error))
        {
            shell.WriteError(error!);
            return;
        }

        shell.WriteLine($"{args[0]} = {args[1]}");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length > 0)
            return CompletionResult.Empty;

        var options = _cfg.AllCVarNames
            .Select(name =>
            {
                _cfg.TryGetByName(name, out var value);
                return new CompletionOption(name, $"{value}");
            })
            .ToList();

        return new CompletionResult(options);
    }
}

public sealed class CvarListCommand : IConsoleCommand
{
    public string Name => "cvars";
    public string Description => "Lists every registered cvar and its current value.";

    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public CvarListCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        foreach (var name in _cfg.AllCVarNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            _cfg.TryGetByName(name, out var value);
            shell.WriteLine($"{name} = {value}");
        }
    }
}
