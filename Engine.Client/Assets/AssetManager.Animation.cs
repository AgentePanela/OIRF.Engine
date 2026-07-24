using Engine.Client.Assets.Animation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace Engine.Client.Assets;

internal sealed partial class AssetManager : IAssetManager
{
    internal readonly Dictionary<string, AnimationDef> _animations = new();

    // natural png key (e.g. "Player/walk") > how to slice it into frames
    private readonly Dictionary<string, SheetSlice> _sheetSources = new();

    // natural png key of a loose frame file (e.g. "Player/idle-1") > target atlas key ("Player/idle-anim-0")
    private readonly Dictionary<string, string> _explicitFrames = new();

    private sealed class SheetSlice
    {
        public required string AnimKey;
        public int FrameWidth;
        public int FrameHeight;
        public int FrameCount;
    }

    public bool TryGetAnimation(string key, [NotNullWhen(true)] out AnimationDef? def)
        => _animations.TryGetValue(key, out def);

    public List<string> GetAnimationKeys() => _animations.Keys.ToList();

    /// <summary>
    /// Scans every info.yml under Resources/Textures and populates _animations,
    /// _sheetSources and _explicitFrames. Must run before the png scan in LoadRawTextures.
    /// </summary>
    private void LoadAnimationInfo()
    {
        _animations.Clear();
        _sheetSources.Clear();
        _explicitFrames.Clear();

        foreach (var file in TexturesResPath.GetFiles("yml"))
        {
            if (Path.GetFileName(file.FilePath) != "info.yml")
                continue;

            try
            {
                ParseInfoFile(file.FilePath, GetFolder(file.Relative));
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to parse animation info '{file.FilePath}': {ex.Message}");
            }
        }
    }

    private void ParseInfoFile(string filePath, string folder)
    {
        using var reader = new StreamReader(filePath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0)
            return;

        if (yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new Exception("info.yml root must be a mapping.");

        if (!root.Children.TryGetValue(new YamlScalarNode("animations"), out var animsNode))
            return;

        if (animsNode is not YamlSequenceNode animSeq)
            throw new Exception("'animations' must be a list.");

        foreach (var entry in animSeq.Children)
        {
            if (entry is not YamlMappingNode map)
                throw new Exception("Each entry under 'animations' must be a mapping.");

            ParseAnimationEntry(map, folder);
        }
        Log.Debug($"Loaded {_animations.Count} animations.");
    }

    private void ParseAnimationEntry(YamlMappingNode map, string folder)
    {
        var id = GetScalar(map, "id", required: true)!;
        var animKey = string.IsNullOrEmpty(folder) ? id : $"{folder}/{id}";

        var spritesheet = GetBool(map, "spritesheet", false);
        var speed = GetFloat(map, "speed", 10f);
        var loop = GetBool(map, "loop", true);
        var frameSpeeds = GetFloatArray(map, "frameSpeeds");

        if (!map.Children.TryGetValue(new YamlScalarNode("files"), out var filesNode))
            throw new Exception($"Animation '{id}' is missing 'files'.");

        if (spritesheet)
        {
            if (filesNode is not YamlScalarNode fileScalar)
                throw new Exception($"Animation '{id}' is a spritesheet, 'files' must be a single file name (no extension).");

            var frameWidth = GetInt(map, "frameWidth", required: true);
            var frameHeight = GetInt(map, "frameHeight", required: true);
            var frameCount = GetInt(map, "frameCount", required: true);
            var sourceKey = string.IsNullOrEmpty(folder) ? fileScalar.Value! : $"{folder}/{fileScalar.Value}";

            _sheetSources[sourceKey] = new SheetSlice
            {
                AnimKey = animKey,
                FrameWidth = frameWidth,
                FrameHeight = frameHeight,
                FrameCount = frameCount,
            };

            _animations[animKey] = new AnimationDef
            {
                Key = animKey,
                FrameCount = frameCount,
                Speed = speed,
                Loop = loop,
                FrameSpeeds = frameSpeeds,
            };
        }
        else
        {
            if (filesNode is not YamlSequenceNode fileSeq)
                throw new Exception($"Animation '{id}' is not a spritesheet, 'files' must be a list of file names (no extension).");

            var names = fileSeq.Children.Select(c => ((YamlScalarNode)c).Value!).ToArray();
            for (int i = 0; i < names.Length; i++)
            {
                var sourceKey = string.IsNullOrEmpty(folder) ? names[i] : $"{folder}/{names[i]}";
                _explicitFrames[sourceKey] = $"{animKey}-{i}";
            }

            _animations[animKey] = new AnimationDef
            {
                Key = animKey,
                FrameCount = names.Length,
                Speed = speed,
                Loop = loop,
                FrameSpeeds = frameSpeeds,
            };
        }
    }

    /// <summary>
    /// Cuts a decoded spritesheet texture into its frames and queues each one into the atlas
    /// under "{AnimKey}-{frame index}". Caller keeps ownership of (and disposes) the source texture.
    /// </summary>
    private void SliceAndQueue(Texture2D source, SheetSlice slice)
    {
        var columns = Math.Max(1, source.Width / slice.FrameWidth);
        var buffer = new Color[slice.FrameWidth * slice.FrameHeight];

        for (int i = 0; i < slice.FrameCount; i++)
        {
            var col = i % columns;
            var row = i / columns;
            var rect = new Rectangle(col * slice.FrameWidth, row * slice.FrameHeight, slice.FrameWidth, slice.FrameHeight);

            source.GetData(0, rect, buffer, 0, buffer.Length);

            var frame = new Texture2D(_graphics, slice.FrameWidth, slice.FrameHeight);
            frame.SetData(buffer);

            _atlas.QueueSprite($"{slice.AnimKey}-{i}", frame);
        }
    }

    private static string GetFolder(string relativeNoExt)
    {
        var idx = relativeNoExt.LastIndexOf('/');
        return idx < 0 ? string.Empty : relativeNoExt[..idx];
    }

    private static string? GetScalar(YamlMappingNode map, string key, bool required)
    {
        if (map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar)
            return scalar.Value;

        if (required)
            throw new Exception($"Missing required field '{key}'.");

        return null;
    }

    private static bool GetBool(YamlMappingNode map, string key, bool fallback)
    {
        var val = GetScalar(map, key, false);
        return val is null ? fallback : bool.Parse(val);
    }

    private static float GetFloat(YamlMappingNode map, string key, float fallback)
    {
        var val = GetScalar(map, key, false);
        return val is null ? fallback : float.Parse(val, CultureInfo.InvariantCulture);
    }

    private static int GetInt(YamlMappingNode map, string key, bool required = false, int fallback = 0)
    {
        var val = GetScalar(map, key, required);
        return val is null ? fallback : int.Parse(val, CultureInfo.InvariantCulture);
    }

    private static float[]? GetFloatArray(YamlMappingNode map, string key)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode(key), out var node) || node is not YamlSequenceNode seq)
            return null;

        return seq.Children.Select(c => float.Parse(((YamlScalarNode)c).Value!, CultureInfo.InvariantCulture)).ToArray();
    }
}
