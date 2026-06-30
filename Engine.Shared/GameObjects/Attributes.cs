using System;

namespace Engine.Shared.GameObjects;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RegisterComponentAttribute(string name) : Attribute
{
    public string Name => name;
}
