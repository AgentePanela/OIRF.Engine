using System;
using System.Collections.Generic;

namespace Engine.Shared.Debug.ViewVariables;

/// <summary>
/// Hides a member from View Variables entirely.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ViewVariablesHiddenAttribute : Attribute;

/// <summary>
/// Shows a member but always as read-only.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ViewVariablesReadOnlyAttribute : Attribute;

// classified from the DECLARED type, not the runtime value
public enum VVValueKind : byte
{
    Null,
    Scalar,
    Enum,
    EntityRef,
    Collection,
    Dictionary,
    Object,
    Error,
}

public sealed class VVValue
{
    public VVValueKind Kind { get; init; } = VVValueKind.Null;
    public string Text { get; init; } = "";
    public string TypeName { get; init; } = "";
    public int Count { get; init; } = -1;
    public object? Local { get; init; }
    public Type? LocalType { get; init; }

    public static VVValue Error(string message) => new() { Kind = VVValueKind.Error, Text = message };
}

public sealed record VVMemberInfo(
    string Name,
    string TypeName,
    Type? LocalType,
    bool CanWrite,
    VVValueKind Kind,
    bool Drillable,
    IReadOnlyList<string>? EnumNames,
    VVPath Path,
    bool CanRemove = false); // only meaningful for a collection/dict element

// Path is set when the group has its own standalone root (a component) - lets the window
// offer "open just this" instead of only showing it inline.
public sealed record VVGroup(string Name, IReadOnlyList<VVMemberInfo> Members, VVPath? Path = null);

// set on a snapshot whose target is itself a collection/dictionary - CanInsert is false for
// arrays (fixed size) and read-only collections
public sealed record VVCollectionInfo(bool IsDictionary, bool CanInsert);

public sealed class VVSnapshot
{
    public required VVPath Path { get; init; }
    public string Title { get; init; } = "";
    public string? Error { get; init; }
    public IReadOnlyList<VVGroup> Groups { get; init; } = Array.Empty<VVGroup>();
    public VVCollectionInfo? Collection { get; init; }

    // does the member list itself need rebuilding - checked every refresh instead of diffing
    public int StructureVersion { get; init; }
}
