using System;
using System.Linq;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.IoC;

namespace Engine.Shared.Console.Commands;

public sealed class DirtyCommand : IConsoleCommand
{
    [Dependency] private readonly EntityManager _entMan = default!;
    [Dependency] private readonly ComponentFactory _compFac = default!;

    public string Name => "dirty";
    public string Description => "Sends the networked components of an entity again, or only one of them.";
    public string Help => "dirty <n<netId>|uid> [component]";

    public bool RequireServerOrSingleplayer => true;

    public DirtyCommand() => IoCManager.ResolveDependencies(this);

    public void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (!TryParseEntity(args[0], out var uid))
        {
            shell.WriteError($"Unknown entity '{args[0]}'.");
            return;
        }

        Type? only = null;
        if (args.Length == 2)
        {
            only = FindComponent(args[1]);
            if (only is null)
            {
                shell.WriteError($"Unknown component '{args[1]}'.");
                return;
            }

            if (!_compFac.IsNetworked(only))
            {
                shell.WriteError($"'{args[1]}' is not networked.");
                return;
            }
        }

        var count = 0;
        foreach (var comp in _entMan.GetEntityComps(uid) ?? [])
        {
            if (only is not null ? comp.GetType() != only : !_compFac.IsNetworked(comp.GetType()))
                continue;

            _entMan.Dirty(uid, comp);
            count++;
        }

        if (only is not null && count == 0)
            shell.WriteError($"{uid} has no '{args[1]}'.");
        else
            shell.WriteLine($"Dirtied {count} component(s) of {uid}.");
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => args.Length switch
    {
        0 => new CompletionResult([], "n<netId> or uid"),
        1 => CompletionResult.FromOptions(_compFac.ComponentsSanitized
            .Where(kvp => _compFac.IsNetworked(kvp.Value))
            .Select(kvp => kvp.Key), "component (all networked if empty)"),
        _ => CompletionResult.Empty,
    };

    // "n12" is the NetEntity 12 (the number drawn over entities), "12" a uid of this side
    private bool TryParseEntity(string arg, out EntityUid uid)
    {
        if (arg.StartsWith('n') && int.TryParse(arg.AsSpan(1), out var netId))
            return _entMan.TryGetEntity(new NetEntity(netId), out uid);

        return EntityUid.TryParse(arg, out uid) && _entMan.HasEntity(uid, out _);
    }

    // the registered name ("Sprite") or the type name ("SpriteComponent"), any case
    private Type? FindComponent(string name)
    {
        foreach (var (key, type) in _compFac.ComponentsSanitized)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase))
                return type;
        }

        return null;
    }
}
