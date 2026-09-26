using System;
using System.Collections.Generic;
using Engine.Shared.Assets;
using Engine.Shared.GameObjects;
using Engine.Shared.GameStates;
using Engine.Shared.Timing;

namespace Engine.Shared.Graphics;

/// <summary>
/// Animate the SpriteComponent and controls their animation.
/// </summary>
[RegisterComponent("Animation"), NetworkedComponent]
public class AnimationComponent : Component
{
    /// <summary>
    /// Key of the animation to play, e.g. "Player/walk-anim". Must match an id defined
    /// in that folder's info.yml. Independent from SpriteComponent.Key, which stays the base sprite
    /// while this animation draws over it.
    /// Leave empty if this entity only animates via <see cref="Layers"/>.
    /// </summary>
    [AnimationKey]
    public string Key { get; set; } = string.Empty;

    public bool Playing { get; set; } = true;

    /// <summary>
    /// Per-entity speed override, in frames per second. Null means "use the speed from info.yml".
    /// Use AnimationSystem.SetSpeed to change it.
    /// </summary>
    public float? SpeedOverride { get; set; } = null;

    /// <summary>
    /// Per-entity loop override. Null means "use the value from info.yml".
    /// Use AnimationSystem.SetLoop to change it.
    /// </summary>
    public bool? LoopOverride { get; set; } = null;

    /// <summary>
    /// The tick playback started on. <see cref="GameTick.Zero"/> until the AnimationSystem stamps it.
    /// </summary>
    internal GameTick StartTick;

    /// <summary>
    /// How far playback had gone when it was paused - a paused animation is not measured from the current tick.
    /// </summary>
    internal uint PausedTicks;

    /// <summary>
    /// Per-layer animations, driving individual entries of the sibling SpriteComponent.Layers
    /// instead of the base sprite. Matched by <see cref="LayerAnimation.LayerId"/> against
    /// SpriteLayer.Id; entries whose id doesn't match any layer are skipped.
    /// Use AnimationSystem.SetLayerAnimation to add/change one.
    /// </summary>
    public List<LayerAnimation> Layers { get; set; } = new();

    public override IComponentState? GetNetState(GameTick fromTick)
    {
        var layers = new LayerAnimationState[Layers.Count];
        for (var i = 0; i < Layers.Count; i++)
            layers[i] = LayerAnimationState.From(Layers[i]);

        return new AnimationComponentState
        {
            Playback = PlaybackState.From(Key, Playing, SpeedOverride, LoopOverride, StartTick, PausedTicks),
            Layers = layers,
        };
    }

    public override void HandleNetState(IComponentState state)
    {
        if (state is not AnimationComponentState s)
            return;

        var p = s.Playback;
        Key = p.Key;
        Playing = p.Playing;
        SpeedOverride = p.HasSpeed ? p.Speed : null;
        LoopOverride = p.HasLoop ? p.Loop : null;
        StartTick = new GameTick(p.StartTick);
        PausedTicks = p.PausedTicks;

        Layers.RemoveAll(l => !Array.Exists(s.Layers, ls => ls.LayerId == l.LayerId));
        foreach (var layerState in s.Layers)
        {
            var layer = Layers.Find(l => l.LayerId == layerState.LayerId);
            if (layer is null)
            {
                layer = new LayerAnimation { LayerId = layerState.LayerId };
                Layers.Add(layer);
            }

            layerState.ApplyTo(layer);
        }
    }
}

/// <summary>
/// Animation state for a single SpriteComponent layer. See AnimationComponent.Layers.
/// </summary>
public class LayerAnimation
{
    /// <summary>
    /// Id of the SpriteLayer (SpriteComponent.Layers) this animation drives.
    /// </summary>
    public string LayerId { get; set; } = string.Empty;

    /// <inheritdoc cref="AnimationComponent.Key"/>
    [AnimationKey]
    public string Key { get; set; } = string.Empty;

    public bool Playing { get; set; } = true;

    /// <inheritdoc cref="AnimationComponent.SpeedOverride"/>
    public float? SpeedOverride { get; set; } = null;

    /// <inheritdoc cref="AnimationComponent.LoopOverride"/>
    public bool? LoopOverride { get; set; } = null;

    /// <inheritdoc cref="AnimationComponent.StartTick"/>
    internal GameTick StartTick;

    /// <inheritdoc cref="AnimationComponent.PausedTicks"/>
    internal uint PausedTicks;
}

[Serializable]
public sealed class AnimationComponentState : IComponentState
{
    public PlaybackState Playback = new();
    public LayerAnimationState[] Layers = [];
}

[Serializable]
public sealed class PlaybackState
{
    public string Key = "";
    public bool Playing;
    public bool HasSpeed;
    public float Speed;
    public bool HasLoop;
    public bool Loop;
    public uint StartTick;
    public uint PausedTicks;

    public static PlaybackState From(string key, bool playing, float? speed, bool? loop, GameTick startTick, uint pausedTicks) => new()
    {
        Key = key,
        Playing = playing,
        HasSpeed = speed is not null,
        Speed = speed ?? 0f,
        HasLoop = loop is not null,
        Loop = loop ?? false,
        StartTick = startTick.Value,
        PausedTicks = pausedTicks,
    };
}

[Serializable]
public sealed class LayerAnimationState
{
    public string LayerId = "";
    public PlaybackState Playback = new();

    public static LayerAnimationState From(LayerAnimation layer) => new()
    {
        LayerId = layer.LayerId,
        Playback = PlaybackState.From(layer.Key, layer.Playing, layer.SpeedOverride, layer.LoopOverride, layer.StartTick, layer.PausedTicks),
    };

    public void ApplyTo(LayerAnimation layer)
    {
        layer.Key = Playback.Key;
        layer.Playing = Playback.Playing;
        layer.SpeedOverride = Playback.HasSpeed ? Playback.Speed : null;
        layer.LoopOverride = Playback.HasLoop ? Playback.Loop : null;
        layer.StartTick = new GameTick(Playback.StartTick);
        layer.PausedTicks = Playback.PausedTicks;
    }
}
