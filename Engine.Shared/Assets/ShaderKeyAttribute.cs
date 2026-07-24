using System;

namespace Engine.Shared.Assets;

/// <summary>
/// Marks a property as holding a shader resource key for fields that aren't typed as
/// ShaderPath directly (e.g. because they live outside Engine.Client and can't reference it).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ShaderKeyAttribute : Attribute { }
