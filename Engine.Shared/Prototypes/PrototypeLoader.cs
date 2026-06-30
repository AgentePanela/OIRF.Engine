using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace Engine.Shared.Prototypes;

/// <summary>
/// Loads raw prototype data from YAML files in the Resources/Prototypes directory.
/// This class only handles parsing — no business logic or type resolution.
/// </summary>
internal sealed class PrototypeLoader
{
    /// <summary>
    /// Load YAML prototype files from a dir and return them as raw, unresolved prototypes.
    /// </summary>
    public List<RawPrototype> LoadProtos(string path)
    {
        var result = new List<RawPrototype>();

        if (!Directory.Exists(path))
            return result;

        var files = Directory.EnumerateFiles(path, "*.yml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(path, "*.yaml", SearchOption.AllDirectories));

        foreach (var file in files)
        {
            try
            {
                LoadFile(file, result);
            }
            catch (Exception ex)
            {
                throw new PrototypeLoadException($"Error loading prototype file '{file}': {ex.Message}", ex);
            }
        }

        return result;
    }

    private static void LoadFile(string filePath, List<RawPrototype> output)
    {
        using var reader = new StreamReader(filePath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0)
            return;

        if (yaml.Documents[0].RootNode is not YamlSequenceNode seq)
            throw new PrototypeLoadException($"Prototype file '{filePath}' root must be a YAML sequence (list).");

        foreach (var node in seq.Children)
        {
            if (node is not YamlMappingNode map)
                throw new PrototypeLoadException($"Each prototype entry in '{filePath}' must be a YAML mapping.");

            output.Add(ParsePrototype(map, filePath));
        }
    }

    private static RawPrototype ParsePrototype(YamlMappingNode node, string sourceFile)
    {
        var proto = new RawPrototype { SourceFile = sourceFile };

        foreach (var entry in node.Children)
        {
            var key = entry.Key.ToString();

            switch (key)
            {
                case "type":
                    proto.Type = entry.Value.ToString();
                    break;

                case "id":
                    proto.ID = entry.Value.ToString();
                    break;

                case "parent":
                    proto.Parents = ParseParents(entry.Value);
                    break;

                case "abstract":
                    proto.Abstract = bool.Parse(entry.Value.ToString()!);
                    break;

                default:
                    proto.Fields[key] = ConvertNode(entry.Value);
                    break;
            }
        }

        return proto;
    }

    private static string[]? ParseParents(YamlNode node)
    {
        if (node is YamlScalarNode scalar)
            return string.IsNullOrWhiteSpace(scalar.Value) ? null : new[] { scalar.Value };

        if (node is YamlSequenceNode seq)
            return seq.Children.Select(x => x.ToString()).ToArray();

        return null;
    }

    private static object ConvertNode(YamlNode node)
    {
        if (node is YamlScalarNode scalar)
            return scalar.Value ?? string.Empty;

        if (node is YamlSequenceNode seq)
            return seq.Children.Select(ConvertNode).ToList();

        if (node is YamlMappingNode map)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in map.Children)
                dict[child.Key.ToString()] = ConvertNode(child.Value);
            return dict;
        }

        throw new PrototypeLoadException($"Unsupported YAML node type: {node.GetType().Name}");
    }
}

/// <summary>
/// Raw, unresolved prototype data parsed from a YAML file.
/// </summary>
public sealed class RawPrototype
{
    public string SourceFile { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ID { get; set; } = string.Empty;
    public string[]? Parents { get; set; }
    public bool Abstract { get; set; }
    public Dictionary<string, object> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
}
