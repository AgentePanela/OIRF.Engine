using System;

namespace Engine.Shared.Assets;

/// <summary>
/// Marks a property as holding an animation key (an id from a folder's info.yml, e.g.
/// "Player/walk-anim") for fields that reference IAssetManager.TryGetAnimation().
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AnimationKeyAttribute : Attribute { }
