using Engine.Server.GameStates;
using Engine.Shared.Audio;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.GameObjects;

namespace Engine.Server.Audio;

/// <inheritdoc cref="SharedAudioSystem"/>
public sealed class AudioSystem : SharedAudioSystem
{
    [Dependency] private readonly PvsSystem _pvs = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private float _pvsRange;

    public override void Init()
    {
        base.Init();
        _cfg.Subs(NetworkingCvars.NetPvsRange, value => _pvsRange = value);
        _cfg.Subs(NetworkingCvars.Tickrate, value => RestampStartTicks(value), false);
    }

    protected override void OnSoundSpawned(EntityUid uid, bool spatial, float maxDistance, IEntityScene? scene)
    {
        if (spatial && maxDistance <= _pvsRange)
            return;

        if (scene is null)
            _pvs.AddGlobalOverride(uid);
        else
            _pvs.AddSceneOverride(scene, uid);
    }
}
