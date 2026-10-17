using System;

namespace Engine.Client.Graphics;

public enum MaskMode : byte
{
    /// <summary>
    /// Drawn as usual, masks are ignored.
    /// </summary>
    None,

    /// <summary>
    /// Nothing is drawn. What would have been becomes part of the mask instead.
    /// </summary>
    Write,

    /// <summary>
    /// Drawn only where the mask is.
    /// </summary>
    Inside,

    /// <summary>
    /// Drawn only where the mask is not.
    /// </summary>
    Outside,
}

/// <summary>
/// Ends a <see cref="IRenderManager.BeginMask"/> when disposed, going back to what was set before it.
/// </summary>
public readonly struct MaskScope : IDisposable
{
    private readonly RenderManager? _renderer;
    private readonly MaskMode _previousMode;
    private readonly byte _previousId;

    internal MaskScope(RenderManager renderer, MaskMode previousMode, byte previousId)
    {
        _renderer = renderer;
        _previousMode = previousMode;
        _previousId = previousId;
    }

    public void Dispose()
        => _renderer?.RestoreMask(_previousMode, _previousId);
}
