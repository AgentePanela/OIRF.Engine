using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.Prototypes;

namespace Engine.Shared.Debug.ViewVariables;

public sealed class ViewVariablesResolver
{
    private readonly EntityManager _entMan;
    private readonly ComponentFactory _compFac;
    private readonly Func<int, object?> _detachedLookup;

    // GetSanitizedByType is an O(n) scan; called once per component per refresh tick
    private readonly Dictionary<Type, string> _sanitizedNameCache = new();

    public ViewVariablesResolver(EntityManager entMan, ComponentFactory compFac, Func<int, object?> detachedLookup)
    {
        _entMan = entMan;
        _compFac = compFac;
        _detachedLookup = detachedLookup;
    }

    public bool TryResolveRoot(VVRoot root, out object? obj, out string? error)
    {
        obj = null;

        switch (root.Kind)
        {
            case VVRootKind.Entity:
            {
                var uid = new EntityUid(root.Uid);
                var ent = _entMan.GetEntity(uid);
                if (ent is null || ent.Deleting)
                {
                    error = $"Entity #{root.Uid} no longer exists.";
                    return false;
                }

                obj = ent;
                error = null;
                return true;
            }
            case VVRootKind.Component:
            {
                var uid = new EntityUid(root.Uid);
                var type = root.ComponentTypeName is null ? null : _compFac.GetTypeByString(root.ComponentTypeName);
                if (type is null || !_entMan.TryComp(uid, type, out var comp))
                {
                    error = $"Component '{root.ComponentTypeName}' not found on entity #{root.Uid}.";
                    return false;
                }

                obj = comp;
                error = null;
                return true;
            }
            case VVRootKind.Detached:
            {
                obj = _detachedLookup(root.DetachedHandle);
                if (obj is null)
                {
                    error = "Detached object is no longer pinned.";
                    return false;
                }

                error = null;
                return true;
            }
            default:
                error = "Unknown VV root kind.";
                return false;
        }
    }

    public bool TryResolve(VVPath path, out object? obj, out string? error)
    {
        if (!TryResolveRoot(path.Root, out obj, out error))
            return false;

        for (var i = 0; i < path.Steps.Count; i++)
        {
            if (obj is null)
            {
                error = $"'{path.Steps[i - 1]}' is null.";
                return false;
            }

            if (!TryGetStep(obj, path.Steps[i], out obj, out error))
                return false;
        }

        return true;
    }

    private static bool TryGetStep(object parent, VVStep step, out object? value, out string? error)
    {
        value = null;

        if (step is not MemberStep member)
        {
            // collection/dictionary drill-down arrives later
            error = $"Step '{step}' is not supported yet.";
            return false;
        }

        try
        {
            var desc = ViewVariablesConvert.ScanMembers(parent.GetType())
                .FirstOrDefault(m => m.Member.Name == member.Name);

            if (desc.Member is null)
            {
                error = $"No member '{member.Name}' on {parent.GetType().Name}.";
                return false;
            }

            value = DataFieldConverter.GetMemberValue(desc.Member, parent);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryWrite(VVPath path, object? newValue, out string? error)
    {
        if (path.Steps.Count == 0)
        {
            error = "Cannot write to a root object directly.";
            return false;
        }

        if (!TryResolveRoot(path.Root, out var root, out error) || root is null)
            return false;

        var lastIndex = path.Steps.Count - 1;
        var chain = new object[path.Steps.Count];
        chain[0] = root;

        for (var i = 0; i < lastIndex; i++)
        {
            if (!TryGetStep(chain[i], path.Steps[i], out var next, out error))
                return false;

            if (next is null)
            {
                error = $"'{path.Steps[i]}' is null.";
                return false;
            }

            chain[i + 1] = next;
        }

        // validate the whole chain before touching anything - an ancestor that turns out
        // read-only should never leave the leaf mutated with nothing to show for it
        if (!IsStepWritable(chain[lastIndex], path.Steps[lastIndex], out error))
            return false;

        for (var i = lastIndex - 1; i >= 0; i--)
        {
            if (!chain[i + 1].GetType().IsValueType)
                break;

            if (!IsStepWritable(chain[i], path.Steps[i], out error))
            {
                error = $"'{path.Steps[i]}' is read-only, so the change wouldn't be written back.";
                return false;
            }
        }

        if (!TrySetStep(chain[lastIndex], path.Steps[lastIndex], newValue, out error))
            return false;

        for (var i = lastIndex - 1; i >= 0; i--)
        {
            if (!chain[i + 1].GetType().IsValueType)
                break;

            if (!TrySetStep(chain[i], path.Steps[i], chain[i + 1], out error))
                return false;
        }

        error = null;
        return true;
    }

    private static bool IsStepWritable(object parent, VVStep step, out string? error)
    {
        error = null;

        if (step is not MemberStep member)
        {
            error = $"Step '{step}' is not supported yet.";
            return false;
        }

        var desc = ViewVariablesConvert.ScanMembers(parent.GetType())
            .FirstOrDefault(m => m.Member.Name == member.Name);

        if (desc.Member is null)
        {
            error = $"No member '{member.Name}' on {parent.GetType().Name}.";
            return false;
        }

        if (!desc.CanWrite)
        {
            error = $"'{member.Name}' is read-only.";
            return false;
        }

        return true;
    }

    private static bool TrySetStep(object parent, VVStep step, object? value, out string? error)
    {
        error = null;

        if (step is not MemberStep member)
        {
            error = $"Step '{step}' is not supported yet.";
            return false;
        }

        try
        {
            var desc = ViewVariablesConvert.ScanMembers(parent.GetType())
                .FirstOrDefault(m => m.Member.Name == member.Name);

            if (desc.Member is null)
            {
                error = $"No member '{member.Name}' on {parent.GetType().Name}.";
                return false;
            }

            DataFieldConverter.SetMemberValue(desc.Member, parent, value);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public VVSnapshot Snapshot(VVPath path)
    {
        if (!TryResolve(path, out var obj, out var error) || obj is null)
        {
            return new VVSnapshot
            {
                Path = path,
                Title = path.ToString(),
                Error = error ?? "Value is null.",
            };
        }

        // an entity root groups by component; everything else is one flat group
        if (path.Steps.Count == 0 && path.Root.Kind == VVRootKind.Entity && obj is Entity ent)
            return SnapshotEntity(path, ent);

        return new VVSnapshot
        {
            Path = path,
            Title = DescribeTitle(path, obj),
            Groups = [new VVGroup("", BuildMembers(path, obj))],
            StructureVersion = obj.GetType().GetHashCode(),
        };
    }

    private VVSnapshot SnapshotEntity(VVPath path, Entity ent)
    {
        var comps = _entMan.GetEntityComps(ent.Uid) ?? [];
        var groups = new List<VVGroup>(comps.Count);
        var structureHash = comps.Count;

        foreach (var comp in comps.OrderBy(c => GetSanitizedName(c.GetType()), StringComparer.OrdinalIgnoreCase))
        {
            var compType = comp.GetType();
            var compPath = VVPath.Of(VVRoot.Component(ent.Uid, compType));

            groups.Add(new VVGroup(GetSanitizedName(compType), BuildMembers(compPath, comp)));
            structureHash = HashCode.Combine(structureHash, compType);
        }

        var title = string.IsNullOrWhiteSpace(ent.Name) ? $"Entity #{ent.Uid.Id}" : $"{ent.Name} (#{ent.Uid.Id})";
        return new VVSnapshot { Path = path, Title = title, Groups = groups, StructureVersion = structureHash };
    }

    public bool TryRead(VVPath path, out VVValue value)
    {
        if (!TryResolve(path, out var obj, out var error))
        {
            value = VVValue.Error(error ?? "unknown error");
            return false;
        }

        // classifies off the runtime type - a bare read has no declared type to go on
        var runtimeType = obj?.GetType() ?? typeof(object);
        value = new VVValue
        {
            Kind = ClassifyKind(runtimeType, obj),
            Text = ViewVariablesConvert.ToText(obj),
            TypeName = runtimeType.Name,
            Count = obj is System.Collections.ICollection col ? col.Count : -1,
            Local = obj,
            LocalType = runtimeType,
        };
        return true;
    }

    private static List<VVMemberInfo> BuildMembers(VVPath basePath, object obj)
    {
        var members = new List<VVMemberInfo>();

        foreach (var desc in ViewVariablesConvert.ScanMembers(obj.GetType()))
        {
            var memberPath = basePath.Member(desc.Member.Name);
            var memberType = DataFieldConverter.GetMemberType(desc.Member);

            object? value;
            try
            {
                value = DataFieldConverter.GetMemberValue(desc.Member, obj);
            }
            catch (Exception ex)
            {
                members.Add(new VVMemberInfo(desc.Member.Name, memberType.Name, memberType, false,
                    VVValueKind.Error, false, null, memberPath));
                Log.Debug($"VV: failed to read {obj.GetType().Name}.{desc.Member.Name}: {ex.Message}");
                continue;
            }

            var kind = ClassifyKind(memberType, value);
            var enumNames = memberType.IsEnum ? Enum.GetNames(memberType) : null;
            var drillable = value is not null &&
                kind is VVValueKind.Object or VVValueKind.Collection or VVValueKind.Dictionary or VVValueKind.EntityRef;

            members.Add(new VVMemberInfo(desc.Member.Name, memberType.Name, memberType, desc.CanWrite,
                kind, drillable, enumNames, memberPath));
        }

        return members;
    }

    private static VVValueKind ClassifyKind(Type declaredType, object? value)
    {
        if (value is null)
            return VVValueKind.Null;

        var t = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

        if (t == typeof(EntityUid))
            return VVValueKind.EntityRef;

        if (t.IsEnum)
            return VVValueKind.Enum;

        if (t.IsPrimitive || t == typeof(string) || t == typeof(decimal))
            return VVValueKind.Scalar;

        if (value is System.Collections.IDictionary)
            return VVValueKind.Dictionary;

        if (value is System.Collections.IEnumerable && t != typeof(string))
            return VVValueKind.Collection;

        return VVValueKind.Object;
    }

    private string GetSanitizedName(Type compType)
    {
        if (_sanitizedNameCache.TryGetValue(compType, out var name))
            return name;

        name = _compFac.GetSanitizedByType(compType) ?? compType.Name;
        _sanitizedNameCache[compType] = name;
        return name;
    }

    private string DescribeTitle(VVPath path, object obj) => path.Root.Kind switch
    {
        VVRootKind.Component => GetSanitizedName(obj.GetType()),
        _ => obj.GetType().Name,
    };
}
