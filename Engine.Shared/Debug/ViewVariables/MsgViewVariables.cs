using System;
using System.Collections.Generic;
using Engine.Shared.Networking;

namespace Engine.Shared.Debug.ViewVariables;

/// <summary>
/// What a <see cref="MsgViewVariablesWrite"/> is asking for. One message covers every mutation so there is a single
/// server handler and a single reply path.
/// </summary>
public enum VVWriteOp : byte
{
    Write,
    Insert,
    RemoveAt,
    AddComponent,
    RemoveComponent,
}

// client -> server: asks for a snapshot of whatever's at path
public sealed partial class MsgViewVariablesRequest : NetMessage
{
    public uint RequestId { get; private set; }
    public byte RootKind { get; private set; }
    public int RootUid { get; private set; }
    public string RootComponent { get; private set; } = "";
    public int RootHandle { get; private set; }
    public byte Side { get; private set; }
    public List<string> StepKinds { get; private set; } = new();
    public List<string> StepArgs { get; private set; } = new();

    public MsgViewVariablesRequest() { }

    public MsgViewVariablesRequest(uint requestId, VVPath path)
    {
        RequestId = requestId;
        (RootKind, RootUid, RootComponent, RootHandle, Side, StepKinds, StepArgs) = path.Flatten();
    }

    public VVPath? ToPath() => VVPath.Unflatten(RootKind, RootUid, RootComponent, RootHandle, Side, StepKinds, StepArgs);
}

/// <summary>
/// server -> client: a whole snapshot, values included. <para/>
/// Everything is parallel lists because that is what the net message generator can serialize, and the member paths are
/// left out on purpose
/// </summary>
public sealed partial class MsgViewVariablesResponse : NetMessage
{
    public uint RequestId { get; private set; }
    public string Error { get; private set; } = "";
    public string Title { get; private set; } = "";

    public List<string> GroupNames { get; private set; } = new();
    public List<int> GroupMemberCounts { get; private set; } = new();
    public List<string> GroupComponents { get; private set; } = new(); // component name, "" for a plain group

    // members of every group, in group order
    public List<string> Names { get; private set; } = new();
    public List<string> TypeNames { get; private set; } = new();
    public List<VVValueKind> Kinds { get; private set; } = new();
    public List<bool> CanWrite { get; private set; } = new();
    public List<bool> CanRemove { get; private set; } = new();
    public List<bool> Drillable { get; private set; } = new();
    public List<string> EnumNames { get; private set; } = new(); // "A|B|C" per member, "" if not an enum
    public List<string> Texts { get; private set; } = new();
    public List<int> Counts { get; private set; } = new();

    // set when the snapshot's own target is a collection
    public bool IsCollection { get; private set; }
    public bool IsDictionary { get; private set; }
    public bool CanInsert { get; private set; }

    public MsgViewVariablesResponse() { }

    public MsgViewVariablesResponse(uint requestId, string error)
    {
        RequestId = requestId;
        Error = error;
    }

    public MsgViewVariablesResponse(uint requestId, VVSnapshot snapshot, Func<VVMemberInfo, VVValue> read)
    {
        RequestId = requestId;
        Title = snapshot.Title;
        Error = snapshot.Error ?? "";

        if (snapshot.Collection is { } collection)
        {
            IsCollection = true;
            IsDictionary = collection.IsDictionary;
            CanInsert = collection.CanInsert;
        }

        foreach (var group in snapshot.Groups)
        {
            GroupNames.Add(group.Name);
            GroupMemberCounts.Add(group.Members.Count);
            GroupComponents.Add(group.Path?.Root.ComponentTypeName ?? "");

            foreach (var member in group.Members)
            {
                var value = read(member);

                Names.Add(member.Name);
                //full names!!
                TypeNames.Add(member.LocalType?.FullName ?? member.TypeName);
                Kinds.Add(member.Kind);
                CanWrite.Add(member.CanWrite);
                CanRemove.Add(member.CanRemove);
                Drillable.Add(member.Drillable);
                EnumNames.Add(member.EnumNames is null ? "" : string.Join("|", member.EnumNames));
                Texts.Add(value.Text);
                Counts.Add(value.Count);
            }
        }
    }
}

/// <summary>
/// client -> server: mutates whatever is at path, according to <see cref="Op"/>.
/// </summary>
public sealed partial class MsgViewVariablesWrite : NetMessage
{
    public uint RequestId { get; private set; }
    public byte RootKind { get; private set; }
    public int RootUid { get; private set; }
    public string RootComponent { get; private set; } = "";
    public int RootHandle { get; private set; }
    public byte Side { get; private set; }
    public List<string> StepKinds { get; private set; } = new();
    public List<string> StepArgs { get; private set; } = new();

    public byte Op { get; private set; }

    /// <summary>
    /// The new value for <see cref="VVWriteOp.Write"/>, the key for <see cref="VVWriteOp.Insert"/>, the component name
    /// for <see cref="VVWriteOp.AddComponent"/>, unused otherwise.
    /// </summary>
    public string Value { get; private set; } = "";

    public MsgViewVariablesWrite() { }

    public MsgViewVariablesWrite(uint requestId, VVPath path, VVWriteOp op, string value = "")
    {
        RequestId = requestId;
        Op = (byte)op;
        Value = value;
        (RootKind, RootUid, RootComponent, RootHandle, Side, StepKinds, StepArgs) = path.Flatten();
    }

    public VVPath? ToPath() => VVPath.Unflatten(RootKind, RootUid, RootComponent, RootHandle, Side, StepKinds, StepArgs);
}
