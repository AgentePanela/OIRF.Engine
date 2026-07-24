using System;

namespace Engine.Shared.Assets;

/// <summary>
/// Marks a property as holding a texture/sprite resource key for fields that arent 
/// typed as SpriteKey directly (e.g. because dont need the sprite or is outside Engine.Client)
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TextureKeyAttribute : Attribute { }
