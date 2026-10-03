using System;
using System.Linq;
using Engine.Shared.Containers;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Engine.Shared.Networking;

namespace Engine.Shared.Console.Commands;

public abstract class ContainerCommandBase : IConsoleCommand
{
    [Dependency] protected readonly EntityManager EntMan = default!;
    [Dependency] private readonly INetManager _netMan = default!;

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string Help { get; }

    protected ContainerCommandBase() => IoCManager.ResolveDependencies(this);

    protected ContainerSystem Containers => EntMan.GetSystem<ContainerSystem>()!;

    private bool IsConnectedClient => _netMan.IsClient && _netMan.MySession is not null;

    public abstract void Execute(IConsoleShell shell, string[] args);

    /// <summary>
    /// "n12" is the NetEntity 12 (the number drawn over entities), "12" a local uid.
    /// </summary>
    protected bool TryParseEntity(IConsoleShell shell, string[] args, string arg, out EntityUid uid)
    {
        uid = EntityUid.Empty;

        if (arg.StartsWith('n') && int.TryParse(arg.AsSpan(1), out var netId))
        {
            if (EntMan.TryGetEntity(new NetEntity(netId), out uid))
                return true;

            // a client may not have it in view, the server will
            if (IsConnectedClient)
            {
                Forward(shell, args);
                return false;
            }
        }
        else if (EntityUid.TryParse(arg, out uid) && EntMan.HasEntity(uid, out _))
        {
            return true;
        }

        shell.WriteError($"Unknown entity '{arg}'.");
        return false;
    }

    /// <summary>
    /// Runs here when this side may change the container, otherwise sends the line to the server.
    /// </summary>
    /// <returns>True if the caller should run the command locally.</returns>
    protected bool RunHereOrForward(IConsoleShell shell, string[] args, EntityUid owner, EntityUid item)
    {
        if (!IsConnectedClient || Containers.CanMutate(owner, item))
            return true;

        Forward(shell, args);
        return false;
    }

    private void Forward(IConsoleShell shell, string[] args)
    {
        // a local uid means nothing on the server
        if (args.Any(a => !a.StartsWith('n') && EntityUid.TryParse(a, out _)))
        {
            shell.WriteError("This needs the server: refer to entities by their net id (n<id>).");
            return;
        }

        shell.RemoteExecuteCommand(string.Join(' ', args.Prepend(Name)));
    }
}

public sealed class ContainerInsertCommand : ContainerCommandBase
{
    public override string Name => "container_insert";
    public override string Description => "Puts an entity into a container, declaring it if missing.";
    public override string Help => "container_insert <owner> <item> [id=debug] [slot]";

    public override void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (!TryParseEntity(shell, args, args[0], out var owner) || !TryParseEntity(shell, args, args[1], out var item))
            return;

        if (!RunHereOrForward(shell, args, owner, item))
            return;

        var id = args.Length >= 3 ? args[2] : "debug";
        var slot = args.Length == 4 && args[3] == "slot";

        BaseContainer container;
        try
        {
            container = slot
                ? Containers.EnsureContainer<ContainerSlot>(owner, id)
                : Containers.EnsureContainer<Container>(owner, id);
        }
        catch (InvalidOperationException e)
        {
            shell.WriteError(e.Message);
            return;
        }

        if (Containers.Insert(item, container))
            shell.WriteLine($"Inserted {item} into {container}.");
        else
            shell.WriteError($"Could not insert {item} into {container}.");
    }
}

public sealed class ContainerRemoveCommand : ContainerCommandBase
{
    public override string Name => "container_remove";
    public override string Description => "Takes an entity out of its container, at the owner position.";
    public override string Help => "container_remove <item>";

    public override void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (!TryParseEntity(shell, args, args[0], out var item))
            return;

        if (!Containers.TryGetContainingContainer(item, out var container))
        {
            shell.WriteError($"{item} is not in a container.");
            return;
        }

        if (!RunHereOrForward(shell, args, container.Owner, item))
            return;

        if (Containers.Remove(item))
            shell.WriteLine($"Removed {item} from {container}.");
        else
            shell.WriteError($"Could not remove {item} from {container}.");
    }
}

public sealed class ContainerEmptyCommand : ContainerCommandBase
{
    public override string Name => "container_empty";
    public override string Description => "Takes everything out of one container, or all of them.";
    public override string Help => "container_empty <owner> [id]";

    public override void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (!TryParseEntity(shell, args, args[0], out var owner))
            return;

        if (!RunHereOrForward(shell, args, owner, owner))
            return;

        foreach (var container in Containers.GetAllContainers(owner).ToList())
        {
            if (args.Length == 2 && container.Id != args[1])
                continue;

            var count = container.Count;
            var all = Containers.EmptyContainer(container);
            shell.WriteLine(all
                ? $"Emptied {container} ({count})."
                : $"Emptied {container} partially, {container.Count} stayed.");
        }
    }
}

public sealed class ContainerListCommand : ContainerCommandBase
{
    public override string Name => "container_list";
    public override string Description => "Lists the containers of an entity as this side sees them.";
    public override string Help => "container_list <owner>";

    public override void Execute(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError($"Usage: {Help}");
            return;
        }

        if (!TryParseEntity(shell, args, args[0], out var owner))
            return;

        var any = false;
        foreach (var container in Containers.GetAllContainers(owner))
        {
            any = true;
            var entities = container.ContainedEntities
                .Select(e => EntMan.TryGetNetEntity(e, out var net) ? $"{e} (n{net.Id})" : e.ToString());

            shell.WriteLine($"{container.Id} [{container.Kind}]: {string.Join(", ", entities)}");
        }

        if (!any)
            shell.WriteLine($"{owner} has no containers.");
    }
}
