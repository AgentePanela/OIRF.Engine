using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Engine.Shared.Assets;
using Engine.Shared.Prototypes;

namespace Engine.Shared.Audio;

public abstract class SoundSpecifier
{
    /// <summary>
    /// Multiplies the volume its played with.
    /// </summary>
    public float Volume { get; set; } = 1f;

    /// <summary>
    /// Added to the pitch its played with, in octaves (-1 to 1).
    /// </summary>
    public float Pitch { get; set; } = 0f;

    internal static SoundSpecifier FromYaml(object rawValue)
    {
        if (rawValue is not IDictionary map)
            return new SoundPathSpecifier(rawValue.ToString()!);

        string? Get(string key)
        {
            foreach (DictionaryEntry entry in map)
            {
                if (string.Equals(entry.Key?.ToString(), key, System.StringComparison.OrdinalIgnoreCase))
                    return entry.Value?.ToString();
            }

            return null;
        }

        var path = Get("path");
        var collection = Get("collection");

        SoundSpecifier result = (path, collection) switch
        {
            ({ } p, null) => new SoundPathSpecifier(p),
            (null, { } c) => new SoundCollectionSpecifier(c),
            (null, null) => throw new PrototypeLoadException("A sound needs either 'path' or 'collection'."),
            _ => throw new PrototypeLoadException("A sound takes 'path' or 'collection', not both."),
        };

        if (Get("volume") is { } volume)
            result.Volume = float.Parse(volume, CultureInfo.InvariantCulture);

        if (Get("pitch") is { } pitch)
            result.Pitch = float.Parse(pitch, CultureInfo.InvariantCulture);

        return result;
    }
}
public sealed class SoundPathSpecifier : SoundSpecifier
{
    [AudioKey]
    public string Path { get; set; } = "";

    public SoundPathSpecifier()
    {
        
    }

    public SoundPathSpecifier(string path) 
        => Path = path;

    public override string ToString() 
        => $"SoundPathSpecifier({Path})";
}

/// <summary>
/// A random file of a <see cref="SoundCollectionPrototype"/>, picked every time it plays.
/// </summary>
public sealed class SoundCollectionSpecifier : SoundSpecifier
{
    public ProtoId<SoundCollectionPrototype> Collection { get; set; }

    public SoundCollectionSpecifier()
    {
        
    }

    public SoundCollectionSpecifier(string collection) 
        => Collection = new ProtoId<SoundCollectionPrototype>(collection);

    public override string ToString() 
        => $"SoundCollectionSpecifier({Collection})";
}
