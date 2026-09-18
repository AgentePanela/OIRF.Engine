using System;
using System.Collections.Generic;
using System.Linq;

namespace Engine.Shared.Debug.ViewVariables;

public enum VVRootKind : byte
{
    Entity,
    Component,
    Detached,
}

public readonly record struct VVRoot(VVRootKind Kind, int Uid, string? ComponentTypeName, int DetachedHandle)
{
    public static VVRoot Entity(EntityUid uid) => new(VVRootKind.Entity, uid.Id, null, 0);

    public static VVRoot Component(EntityUid uid, Type compType) => new(VVRootKind.Component, uid.Id, compType.Name, 0);

    public static VVRoot Detached(int handle) => new(VVRootKind.Detached, 0, null, handle);

    public override string ToString() => Kind switch
    {
        VVRootKind.Entity => $"#{Uid}",
        VVRootKind.Component => $"#{Uid}/{ComponentTypeName}",
        VVRootKind.Detached => $"detached:{DetachedHandle}",
        _ => "?",
    };
}

// never a captured object - a struct member read through reflection is a boxed copy, so editing
// it has to walk back up the chain to write the mutated copy into its parent
public abstract record VVStep;

public sealed record MemberStep(string Name) : VVStep
{
    public override string ToString() => Name;
}

public sealed record IndexStep(int Index) : VVStep
{
    public override string ToString() => $"[{Index}]";
}

public sealed record KeyStep(string RawKey) : VVStep
{
    public override string ToString() => $"[{RawKey}]";
}

/// <summary>
/// A root plus a chain of steps. Not a string, ToString is display-only - but plain
/// and serializable, so a remote VV can reuse it as-is.
/// </summary>
public sealed class VVPath : IEquatable<VVPath>
{
    public const int MaxDepth = 32;

    public VVRoot Root { get; }
    public IReadOnlyList<VVStep> Steps { get; }

    private VVPath(VVRoot root, IReadOnlyList<VVStep> steps)
    {
        Root = root;
        Steps = steps;
    }

    public static VVPath Of(VVRoot root) => new(root, Array.Empty<VVStep>());

    public VVPath Member(string name) => Append(new MemberStep(name));

    public VVPath At(int index) => Append(new IndexStep(index));

    public VVPath At(string rawKey) => Append(new KeyStep(rawKey));

    private VVPath Append(VVStep step)
    {
        if (Steps.Count >= MaxDepth)
            throw new InvalidOperationException($"VVPath exceeded MaxDepth ({MaxDepth}).");

        return new VVPath(Root, [.. Steps, step]);
    }

    public VVPath? Parent => Steps.Count == 0 ? null : new VVPath(Root, Steps.Take(Steps.Count - 1).ToArray());

    public override string ToString()
        => Steps.Count == 0 ? Root.ToString() : $"{Root} / {string.Join(" / ", Steps)}";

    public bool Equals(VVPath? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (!Root.Equals(other.Root) || Steps.Count != other.Steps.Count)
            return false;

        for (var i = 0; i < Steps.Count; i++)
        {
            if (!Steps[i].Equals(other.Steps[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as VVPath);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Root);
        foreach (var step in Steps)
            hash.Add(step);

        return hash.ToHashCode();
    }

    // flattened into primitive lists so a VVPath can ride inside a NetMessage
    public (byte RootKind, int RootUid, string RootComponent, int RootHandle, List<string> StepKinds, List<string> StepArgs) Flatten()
    {
        var kinds = new List<string>(Steps.Count);
        var args = new List<string>(Steps.Count);

        foreach (var step in Steps)
        {
            switch (step)
            {
                case MemberStep m: kinds.Add("M"); args.Add(m.Name); break;
                case IndexStep i: kinds.Add("I"); args.Add(i.Index.ToString()); break;
                case KeyStep k: kinds.Add("K"); args.Add(k.RawKey); break;
            }
        }

        return ((byte)Root.Kind, Root.Uid, Root.ComponentTypeName ?? "", Root.DetachedHandle, kinds, args);
    }

    public static VVPath? Unflatten(byte rootKind, int rootUid, string rootComponent, int rootHandle,
        List<string> stepKinds, List<string> stepArgs)
    {
        if (stepKinds.Count != stepArgs.Count || stepKinds.Count > MaxDepth || !Enum.IsDefined(typeof(VVRootKind), rootKind))
            return null;

        VVRoot root = (VVRootKind)rootKind switch
        {
            VVRootKind.Entity => new VVRoot(VVRootKind.Entity, rootUid, null, 0),
            VVRootKind.Component => new VVRoot(VVRootKind.Component, rootUid, rootComponent, 0),
            _ => new VVRoot(VVRootKind.Detached, 0, null, rootHandle),
        };

        var path = Of(root);
        for (var i = 0; i < stepKinds.Count; i++)
        {
            switch (stepKinds[i])
            {
                case "M": path = path.Member(stepArgs[i]); break;
                case "K": path = path.At(stepArgs[i]); break;
                case "I" when int.TryParse(stepArgs[i], out var index): path = path.At(index); break;
                default: return null;
            }
        }

        return path;
    }
}
