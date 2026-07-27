using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Engine.ResourcesBuilder;

/// <summary>
/// Gives sprite shaders lighting support without the shader author having to
/// write any lighting code themselves - modeled on how RobustToolbox's own
/// shader compiler wraps every custom shader with its lighting logic unless
/// it opts out.
///
/// Runs entirely in memory against a copy of each `.fx` source; the original
/// files under Shaders/ are never modified. For each file:
/// 1. If it declares `technique Unshaded`, it's left untouched (opt-out).
/// 2. Otherwise, the pixel shader entry function(s) referenced by
///    `PixelShader = compile SM Name();` are renamed to `Name_User` (only the
///    function *definitions* - the technique's own reference keeps pointing
///    at `Name`, which becomes the injected wrapper below).
/// 3. A wrapper function named `Name` is injected that calls `Name_User(...)`
///    and multiplies its result by the lightmap sampled at the pixel's
///    screen position, so the shader's own author never has to know about
///    lighting at all. All injected declarations (LightMap/samplers/etc.)
///    and the wrapper itself are inserted right before the `technique` block
///    - i.e. after every declaration the shader's own author wrote. This
///    matters: ps_3_0 assigns texture/sampler registers implicitly by
///    declaration order, and SpriteBatch always binds the sprite's own
///    texture to register 0 - inserting LightMap earlier would shift the
///    shader's own SpriteTexture off register 0 and silently read garbage.
///
/// Comments are stripped from the source before any of this runs (see
/// <see cref="StripComments"/>), both so "technique"/"PixelShader = compile"
/// occurrences inside comments can't confuse detection, and so the shader
/// text cached on disk doesn't carry the original author's comments.
///
/// Known limitation: assumes a single relevant `technique` block per file -
/// true for every shader in this project today (Grayscale.fx,
/// MetallicFloor.fx), revisit if a future shader breaks that assumption.
/// </summary>
public static class ShaderLightingInjector
{
    private static readonly Regex UnshadedOptOut = new(@"technique\s+Unshaded\b", RegexOptions.Compiled);
    private static readonly Regex AlreadyLit = new(@"\bTexture2D\s+LightMap\b", RegexOptions.Compiled);
    private static readonly Regex SpriteVertexOutput = new(@"\bstruct\s+VertexShaderOutput\b", RegexOptions.Compiled);
    private static readonly Regex EntryPointRef = new(@"PixelShader\s*=\s*compile\s+\S+\s+(\w+)\s*\(\s*\)\s*;", RegexOptions.Compiled);
    private static readonly Regex FirstTechnique = new(@"\btechnique\b", RegexOptions.Compiled);

    // Matches a string literal OR a comment; string literals are kept as-is in the
    // replacement callback so a "//" or "/*" inside a quoted string isn't treated as
    // a comment start. Comments are replaced with a space (not "") so removing e.g.
    // "int/* x */Name" can't accidentally glue two tokens into one identifier.
    private static readonly Regex CommentOrString = new(
        @"""(?:\\.|[^""\\])*""|/\*.*?\*/|//[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static string StripComments(string source)
        => CommentOrString.Replace(source, m => m.Value[0] == '"' ? m.Value : " ");

    // Some shaders already declare one of these for their own purposes (e.g.
    // MetallicFloor.fx already has its own ViewportSize for a screen-space
    // effect) - only inject the pieces that aren't already present, so we
    // never emit a duplicate global declaration.
    private static readonly (string Name, string DeclarationPattern, string Declaration)[] InjectableGlobals =
    {
        // LightingEnabled must be checked before sampling LightMap: this
        // wrapper is baked into the compiled shader unconditionally, so when
        // the game's lighting system is off, LightMap is never bound to a
        // real texture - sampling it anyway would come back black and
        // multiply every sprite using this shader to solid black.
        ("LightingEnabled", @"\bbool\s+LightingEnabled\b", "bool LightingEnabled = false;"),
        ("LightMap", @"\bTexture2D\s+LightMap\b", "Texture2D LightMap;"),
        ("ViewportSize", @"\bfloat2\s+ViewportSize\b", "float2 ViewportSize = float2(1280.0, 720.0);"),
        ("PixelatedLighting", @"\bbool\s+PixelatedLighting\b", "bool PixelatedLighting = false;"),
        ("LightSampler", @"\bsampler2D\s+LightSampler\b", "sampler2D LightSampler = sampler_state\n{\n    Texture = <LightMap>;\n    MinFilter = Linear;\n    MagFilter = Linear;\n};"),
        ("LightSamplerPoint", @"\bsampler2D\s+LightSamplerPoint\b", "sampler2D LightSamplerPoint = sampler_state\n{\n    Texture = <LightMap>;\n    MinFilter = Point;\n    MagFilter = Point;\n};"),
    };

    private static string BuildDeclarations(string source)
    {
        var missing = InjectableGlobals
            .Where(g => !Regex.IsMatch(source, g.DeclarationPattern))
            .Select(g => g.Declaration)
            .ToList();

        if (missing.Count == 0)
            return "";

        return "\n// ---- injected by ShaderLightingInjector ----\n" + string.Join("\n\n", missing) + "\n";
    }

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
    /// Applies the rename+wrap transformation described in the class remarks
    /// to a single shader source string. Pure function - safe to unit test
    /// directly against known shader text.
    /// </summary>
    public static string Transform(string source)
    {
        source = StripComments(source);

        if (UnshadedOptOut.IsMatch(source))
            return source;

        // Already hand-written with lighting support (e.g. DefaultLit.fx) -
        // don't wrap it again, or it would sample+multiply the lightmap twice.
        if (AlreadyLit.IsMatch(source))
            return source;

        // Not a sprite shader at all (e.g. the internal lighting-computation
        // shaders - LightSoft.fx, ShadowDepth.fx, LightBlur.fx, WallMerge.fx -
        // use their own vertex formats, not SpriteBatch's VertexShaderOutput).
        // Wrapping those would reference a struct that doesn't exist there.
        if (!SpriteVertexOutput.IsMatch(source))
            return source;

        var funcNames = EntryPointRef.Matches(source)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        if (funcNames.Count == 0)
            return source;

        // Declarations MUST come after the shader's own SpriteTexture/sampler
        // declarations, not before: profiles like ps_3_0 assign texture/sampler
        // registers implicitly by declaration order, and MonoGame's SpriteBatch
        // always binds the sprite's own texture to register 0 regardless of
        // the effect in use. Inserting LightMap/LightSampler earlier than the
        // shader's own SpriteTexture would shift it off register 0 and make it
        // read nothing (black) while LightSampler reads the sprite texture by
        // accident. Inserting everything right before `technique` (after all
        // of the shader's own declarations) keeps SpriteTexture at register 0.
        var declarations = BuildDeclarations(source);
        var result = source;

        var wrappers = new StringBuilder(declarations);
        foreach (var name in funcNames)
        {
            bool hasVposVariant = Regex.IsMatch(result, $@"float4\s+{Regex.Escape(name)}\s*\([^)]*:\s*VPOS[^)]*\)");

            result = new Regex($@"\bfloat4(\s+){Regex.Escape(name)}(\s*\()")
                .Replace(result, $"float4$1{name}_User$2");

            var openglCall = hasVposVariant ? $"{name}_User(input, vpos)" : $"{name}_User(input)";

            wrappers.Append($@"
// ---- injected by ShaderLightingInjector ----
#if OPENGL
float4 {name}(VertexShaderOutput input, float2 vpos : VPOS) : COLOR
{{
    float4 color = {openglCall};
    // Ternary, not an if() - texture samples inside a real branch are
    // unreliable under ps_3_0/OpenGL. Always sample, then select.
    float2 uv = vpos / ViewportSize;
    float3 sampledLight = PixelatedLighting ? tex2D(LightSamplerPoint, uv).rgb : tex2D(LightSampler, uv).rgb;
    float3 light = LightingEnabled ? sampledLight : float3(1.0, 1.0, 1.0);
    return float4(color.rgb * light, color.a);
}}
#else
float4 {name}(VertexShaderOutput input) : COLOR
{{
    float4 color = {name}_User(input);
    float2 uv = input.Position.xy / ViewportSize;
    float3 sampledLight = PixelatedLighting ? tex2D(LightSamplerPoint, uv).rgb : tex2D(LightSampler, uv).rgb;
    float3 light = LightingEnabled ? sampledLight : float3(1.0, 1.0, 1.0);
    return float4(color.rgb * light, color.a);
}}
#endif
");
        }

        result = FirstTechnique.Replace(result, m => wrappers + m.Value, 1);

        return result;
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
