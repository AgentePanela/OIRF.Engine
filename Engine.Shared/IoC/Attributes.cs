using System;

/// <summary>
/// Marks a field or property to be automatically resolved by the IoC container.
/// </summary>
/// <remarks>
/// When <see cref="IoCManager.ResolveDependencies(object)"/> is called,
/// any field or property decorated with <see cref="DependencyAttribute"/>
/// will automatically receive an instance from the IoC container.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DependencyAttribute : Attribute
{
}

namespace Engine.Shared.IoC
{
    /// <summary>
    /// Marks a class to be automatically registered in the IoC container.
    /// </summary>
    /// <remarks>
    /// Classes decorated with this attribute can be discovered and registered
    /// automatically using <see cref="IoCManager.AutoRegister(System.Reflection.Assembly)"/>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RegisterIoCAttribute : Attribute
    {
        public bool Singleton { get; }
        public Type? RegisterAs { get; }

        public RegisterIoCAttribute(Type? registerAs, bool singleton = true)
        {
            RegisterAs = registerAs;
            Singleton = singleton;
        }

        public RegisterIoCAttribute(bool singleton = true)
        {
            Singleton = singleton;
        }
    }
}
