using System;

namespace Engine.Shared.Prototypes;

/// <summary>
/// Base interface for all prototype definitions.
/// Every prototype must declare a YAML type key and a unique string ID.
/// </summary>
public interface IPrototype
{
    /// <summary>
    /// The YAML type discriminator (e.g. "entity", "randomWeights").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Unique identifier for this prototype instance.
    /// </summary>
    string ID { get; }
}

/// <summary>
/// Interface for prototypes that support inheritance via parent references.
/// </summary>
public interface IInheritingPrototype
{
    /// <summary>
    /// IDs of parent prototypes to inherit data from.
    /// </summary>
    string[]? Parents { get; }

    /// <summary>
    /// If true, this prototype is abstract and cannot be indexed or instantiated.
    /// </summary>
    bool Abstract { get; }
}

/// <summary>
/// Marks a class as a prototype type that can be loaded from YAML.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PrototypeAttribute : Attribute
{
    /// <summary>
    /// The YAML type key that maps to this prototype class (e.g. "entity").
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Load priority. Lower values are loaded first. Default is 0.
    /// </summary>
    public int LoadPriority { get; }

    public PrototypeAttribute(string type, int loadPriority = 0)
    {
        Type = type;
        LoadPriority = loadPriority;
    }
}

/// <summary>
/// Marks a field or property on a prototype class as a data-bound field
/// that gets populated from YAML data.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DataFieldAttribute : Attribute
{
    /// <summary>
    /// The YAML key name. If null, uses the member name.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// If true, the field must be present in the YAML data.
    /// </summary>
    public bool Required { get; }

    public DataFieldAttribute(string? name = null, bool required = false)
    {
        Name = name;
        Required = required;
    }
}

/// <summary>
/// Marks a [DataField] that holds a list of component-like mappings.
/// During prototype inheritance, entries are merged by their "type" key
/// instead of replacing the entire list.
/// The DataField name is used to identify which YAML key triggers the merge.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ComponentsDataFieldAttribute : Attribute { }

/// <summary>
/// Exception type for prototype loading errors.
/// </summary>
public sealed class PrototypeLoadException : Exception
{
    public PrototypeLoadException(string message) : base(message) { }
    public PrototypeLoadException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Exeption for unknow prototype id or types.
/// </summary>
public sealed class UnknowPrototypeException : Exception
{
    public UnknowPrototypeException(string message) : base(message) { }
    public UnknowPrototypeException(string message, Exception inner) : base(message, inner) { }
}
