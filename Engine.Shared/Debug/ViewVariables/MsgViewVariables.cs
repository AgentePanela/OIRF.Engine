using System.Collections.Generic;
using Engine.Shared.Networking;

namespace Engine.Shared.Debug.ViewVariables;

// client -> server: asks for a snapshot of whatever's at path
public sealed partial class MsgViewVariablesRequest : NetMessage
{
    public uint RequestId { get; private set; }
    public byte RootKind { get; private set; }
    public int RootUid { get; private set; }
    public string RootComponent { get; private set; } = "";
    public int RootHandle { get; private set; }
    public List<string> StepKinds { get; private set; } = new();
    public List<string> StepArgs { get; private set; } = new();

    public MsgViewVariablesRequest() { }

    public MsgViewVariablesRequest(uint requestId, VVPath path)
    {
        RequestId = requestId;
        (RootKind, RootUid, RootComponent, RootHandle, StepKinds, StepArgs) = path.Flatten();
    }

    public VVPath? ToPath() => VVPath.Unflatten(RootKind, RootUid, RootComponent, RootHandle, StepKinds, StepArgs);
}

/// <summary>
/// server -> client: the snapshots metadata (no live values - those still go through a per-path
/// TryRead, which has no remote transport yet)
/// </summary>
public sealed partial class MsgViewVariablesResponse : NetMessage
{
    public uint RequestId { get; private set; }
    public string Error { get; private set; } = "";
    public string Title { get; private set; } = "";
    public List<string> Names { get; private set; } = new();
    public List<string> TypeNames { get; private set; } = new();
    public List<VVValueKind> Kinds { get; private set; } = new();
    public List<bool> CanWrite { get; private set; } = new();
    public List<string> EnumNames { get; private set; } = new(); // "A|B|C" per member, "" if not an enum

    public MsgViewVariablesResponse() { }

    public MsgViewVariablesResponse(uint requestId, VVSnapshot snapshot)
    {
        RequestId = requestId;
        Title = snapshot.Title;
        Error = snapshot.Error ?? "";

        foreach (var group in snapshot.Groups)
        {
            foreach (var member in group.Members)
            {
                Names.Add(member.Name);
                TypeNames.Add(member.TypeName);
                Kinds.Add(member.Kind);
                CanWrite.Add(member.CanWrite);
                EnumNames.Add(member.EnumNames is null ? "" : string.Join("|", member.EnumNames));
            }
        }
    }
}

/// <summary>
/// client -> server: write text into whatevers at path. No reply message yet - nothing on the
/// server side actually applies this (see ViewVariablesManager.Init).
/// </summary>
public sealed partial class MsgViewVariablesWrite : NetMessage
{
    public uint RequestId { get; private set; }
    public byte RootKind { get; private set; }
    public int RootUid { get; private set; }
    public string RootComponent { get; private set; } = "";
    public int RootHandle { get; private set; }
    public List<string> StepKinds { get; private set; } = new();
    public List<string> StepArgs { get; private set; } = new();
    public string Value { get; private set; } = "";

    public MsgViewVariablesWrite() { }

    public MsgViewVariablesWrite(uint requestId, VVPath path, string value)
    {
        RequestId = requestId;
        Value = value;
        (RootKind, RootUid, RootComponent, RootHandle, StepKinds, StepArgs) = path.Flatten();
    }

    public VVPath? ToPath() => VVPath.Unflatten(RootKind, RootUid, RootComponent, RootHandle, StepKinds, StepArgs);
}
