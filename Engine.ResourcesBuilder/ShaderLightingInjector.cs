using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Engine.ResourcesBuilder;

/// <summary>
/// Pre-processes every `.fx` source before it reaches the content builder.
/// Runs entirely in memory against a copy of each file; the originals under
/// Shaders/ are never modified.
///
/// Comments are stripped from the source (see <see cref="StripComments"/>) so
/// the shader text cached on disk doesn't carry the original author's
/// comments.
/// </summary>
// todo: light rework - this used to wrap sprite shaders with lightmap sampling
public static class ShaderLightingInjector
{
    // Matches a string literal OR a comment; string literals are kept as-is in the
    // replacement callback so a "//" or "/*" inside a quoted string isn't treated as
    // a comment start. Comments are replaced with a space (not "") so removing e.g.
    // "int/* x */Name" can't accidentally glue two tokens into one identifier.
    private static readonly Regex CommentOrString = new(
        @"""(?:\\.|[^""\\])*""|/\*.*?\*/|//[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static string StripComments(string source)
        => CommentOrString.Replace(source, m => m.Value[0] == '"' ? m.Value : " ");

    /// <summary>
    /// Reads every `.fx` file directly under `sourceRoot/Shaders`, applies
    /// <see cref="Transform"/>, and writes the result to a short-lived temp
    /// folder the content builder can be pointed at instead of the real
    /// source. Returns that temp folder (or <paramref name="sourceRoot"/>
    /// unchanged if there's no Shaders folder to process - callers must not
    /// delete the returned path in that case).
    ///
    /// The transformed source is also encrypted at rest under
    /// <paramref name="baseDir"/> (not real security - the key ships in this
    /// assembly - just keeps plain shader text from sitting on disk between
    /// builds) alongside a content-hash manifest, so a shader whose original
    /// `.fx` hasn't changed since last time is skipped entirely: it's neither
    /// re-transformed nor written to the temp folder, so the content builder
    /// never sees it and leaves its already-compiled `.xnb` alone.
    /// </summary>
    public static string GenerateRoot(string sourceRoot, string baseDir)
    {
        var shadersDir = Path.Combine(sourceRoot, "Shaders");
        if (!Directory.Exists(shadersDir))
            return sourceRoot;

        var absoluteBaseDir = Path.IsPathRooted(baseDir) ? baseDir : Path.Combine(AppContext.BaseDirectory, baseDir);
        var cacheRoot = Path.Combine(absoluteBaseDir, SafeFolderName(sourceRoot));
        Directory.CreateDirectory(cacheRoot);

        var manifestPath = Path.Combine(cacheRoot, "manifest.json");
        Dictionary<string, string> manifest = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifestPath)) ?? []
            : [];

        var buildDir = Path.Combine(Path.GetTempPath(), "ShaderBuild", SafeFolderName(sourceRoot));
        if (Directory.Exists(buildDir))
            Directory.Delete(buildDir, recursive: true);
        Directory.CreateDirectory(buildDir);

        var manifestChanged = false;

        // A shader that was removed or renamed in Shaders/ leaves its old .enc, its old
        // compiled .xnb and its manifest entry behind forever otherwise - nothing else
        // ever revisits a name once it drops out of the current file list.
        var currentNames = Directory.GetFiles(shadersDir, "*.fx", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToHashSet();

        foreach (var stale in manifest.Keys.Where(n => !currentNames.Contains(n)).ToList())
        {
            manifest.Remove(stale);
            manifestChanged = true;

            var staleEnc = Path.Combine(cacheRoot, stale + ".enc");
            if (File.Exists(staleEnc))
                File.Delete(staleEnc);

            var staleXnb = Path.Combine(absoluteBaseDir, Path.ChangeExtension(stale, ".xnb"));
            if (File.Exists(staleXnb))
                File.Delete(staleXnb);
        }

        foreach (var file in Directory.GetFiles(shadersDir, "*.fx", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            var raw = File.ReadAllBytes(file);
            var hash = Convert.ToHexString(SHA256.HashData(raw));
            var encPath = Path.Combine(cacheRoot, name + ".enc");

            // Unchanged since last time and we still have its encrypted cache entry -
            // skip re-transforming and don't write it to buildDir, so the content
            // builder's wildcard scan never sees it and its .xnb is left as-is.
            if (manifest.TryGetValue(name, out var cachedHash) && cachedHash == hash && File.Exists(encPath))
                continue;

            var transformed = Transform(Encoding.UTF8.GetString(raw));
            File.WriteAllText(Path.Combine(buildDir, name), transformed);
            File.WriteAllBytes(encPath, ShaderCrypto.Encrypt(Encoding.UTF8.GetBytes(transformed)));

            manifest[name] = hash;
            manifestChanged = true;
        }

        if (manifestChanged)
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

        return buildDir;
    }

    /// <summary>
    /// Applies the pre-processing described in the class remarks to a single
    /// shader source string. Pure function - safe to unit test directly
    /// against known shader text.
    /// </summary>
    public static string Transform(string source)
    {
        // todo: light rework
        return StripComments(source);
    }

    private static string SafeFolderName(string path)
    {
        var full = Path.GetFullPath(path);
        var leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
        // string.GetHashCode() is randomized per process - use a stable hash so the
        // generated folder is reused across runs instead of piling up new ones each launch.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..8];
        return $"{leaf}_{hash}";
    }
}
