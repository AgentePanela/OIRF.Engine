using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.Prototypes;

namespace Engine.Shared.Tags;

public sealed class TagSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Init()
    {
        base.Init();

#if DEBUG
        SubscribeEvent<TagComponent, CompAddedEvent>(OnTagInit);
#endif
    }

    private void OnTagInit(EntityUid uid, TagComponent component, CompAddedEvent args)
    {
        foreach (var tag in component.Tags)
            AssertInvalidTag(tag, uid);
    }

    private void AssertInvalidTag(string id, EntityUid uid)
    {
        if (!_proto.HasIndex<TagPrototype>(id))
            throw new UnknowPrototypeException($"Unknow tag prototype {id} in entity {uid}!");
    }

    public bool HasTag(EntityUid uid, ProtoId<TagPrototype> tag)
    {
        if (!TryComp<TagComponent>(uid, out var comp))
            return false;

        return comp.Tags.Contains(tag);
    }

    public bool HasTags(EntityUid uid, params ProtoId<TagPrototype>[] tags)
    {
        if (!TryComp<TagComponent>(uid, out var comp))
            return false;
        
        foreach (var tag in tags)
        {
            if (!comp.Tags.Contains(tag))
                return false;
        }

        return true;
    }

    public void AddTag(EntityUid uid, ProtoId<TagPrototype> tag)
    {
        var comp = EnsureComp<TagComponent>(uid);
        comp.Tags.Add(tag);
        AssertInvalidTag(tag, uid);
    }

    public void RemoveTag(EntityUid uid, ProtoId<TagPrototype> tag)
    {
        if (!TryComp<TagComponent>(uid, out var comp))
            return;
        comp.Tags.Remove(tag);
    }

    public IEnumerable<EntityUid> GetEntitiesWithTag(ProtoId<TagPrototype> tag)
    {
        foreach (var (uid, comp) in GetEntitiesWithComp<TagComponent>())
        {
            if (comp.Tags.Contains(tag))
                yield return uid;
        }
    }

    public IEnumerable<EntityUid> GetEntitiesWithTags(params ProtoId<TagPrototype>[] tags)
    {
        var query = GetEntitiesWithComp<TagComponent>();
        foreach (var (uid, comp) in query)
        {
            bool hasAll = true;

            foreach (var tag in tags)
            {
                if (!comp.Tags.Contains(tag))
                {
                    hasAll = false;
                    break;
                }
            }

            if (hasAll)
                yield return uid;
        }
    }
}
