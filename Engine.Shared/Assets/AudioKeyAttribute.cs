using System;

namespace Engine.Shared.Assets;

/// <summary>
/// Marks a plain <c>string</c> property as holding an audio resource key (relative to the
/// "Audio" resource root).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AudioKeyAttribute : Attribute { }
