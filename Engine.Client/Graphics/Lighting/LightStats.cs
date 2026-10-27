namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// What the lighting did in one frame.
/// </summary>
public sealed class LightStats
{
    public bool CacheEnabled;
    public int Lights;
    public int StaticLights;

    /// <summary>
    /// Lights that moved or changed lately, drawn every frame.
    /// </summary>
    public int DynamicLights;

    /// <summary>
    /// Lights drawn with their shadows this frame
    /// </summary>
    public int LightsDrawn;

    /// <summary>
    /// Occluder edges that threw a shadow, every light drawn added up.
    /// </summary>
    public int ShadowSegments;

    /// <summary>
    /// Pieces of the world whose static light is kept.
    /// </summary>
    public int ChunksResident;

    public int ChunkCapacity;

    /// <summary>
    /// Pieces made again this frame, because a static light in them changed.
    /// </summary>
    public int ChunksRebuilt;

    /// <summary>
    /// False when the view needs more pieces than fit: the static lights are added to the light map one by one.
    /// </summary>
    public bool ChunksInUse;

    public int StaticAtlasPages;
    public int DynamicAtlasPages;

    /// <summary>
    /// Pixels of light map filled, every viewport added up.
    /// </summary>
    public int LightMapTexels;

    /// <summary>
    /// Video memory of the atlases and the pieces.
    /// </summary>
    public long CacheBytes;

    /// <summary>
    /// Drawing the lights, the static ones and the pieces: once per frame for every viewport.
    /// </summary>
    public double PrepareMs;

    /// <summary>
    /// Putting it together in the light map of every viewport.
    /// </summary>
    public double LightMapMs;

    internal void Reset()
    {
        CacheEnabled = ChunksInUse = false;
        Lights = StaticLights = DynamicLights = LightsDrawn = ShadowSegments = 0;
        ChunksResident = ChunkCapacity = ChunksRebuilt = 0;
        StaticAtlasPages = DynamicAtlasPages = LightMapTexels = 0;
        CacheBytes = 0;
        PrepareMs = LightMapMs = 0;
    }

    internal void CopyFrom(LightStats other)
    {
        CacheEnabled = other.CacheEnabled;
        Lights = other.Lights;
        StaticLights = other.StaticLights;
        DynamicLights = other.DynamicLights;
        LightsDrawn = other.LightsDrawn;
        ShadowSegments = other.ShadowSegments;
        ChunksResident = other.ChunksResident;
        ChunkCapacity = other.ChunkCapacity;
        ChunksRebuilt = other.ChunksRebuilt;
        ChunksInUse = other.ChunksInUse;
        StaticAtlasPages = other.StaticAtlasPages;
        DynamicAtlasPages = other.DynamicAtlasPages;
        LightMapTexels = other.LightMapTexels;
        CacheBytes = other.CacheBytes;
        PrepareMs = other.PrepareMs;
        LightMapMs = other.LightMapMs;
    }
}
