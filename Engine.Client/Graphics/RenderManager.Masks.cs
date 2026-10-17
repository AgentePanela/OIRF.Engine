using System;
using System.Collections.Generic;
using Engine.Client.Graphics.Shaders;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

public sealed partial class RenderManager
{
    // What is stamped on everything submitted right now, set by BeginMask.
    private MaskMode _maskMode;
    private byte _maskId;

    private readonly Dictionary<(MaskMode Mode, byte Id), DepthStencilState> _maskStates = new();

    /// <summary>
    /// Writes to the mask only, leaving the color of the target alone.
    /// </summary>
    private static readonly BlendState MaskWriteBlend = new()
    {
        ColorWriteChannels = ColorWriteChannels.None,
    };

    public MaskScope BeginMask(MaskMode mode, int id = 1)
    {
        var scope = new MaskScope(this, _maskMode, _maskId);
        _maskMode = mode;
        _maskId = (byte)Math.Clamp(id, 1, 255);
        return scope;
    }

    internal void RestoreMask(MaskMode mode, byte id)
    {
        _maskMode = mode;
        _maskId = id;
    }

    private void StampMask(ref DrawCommand command)
    {
        command.Mask = _maskMode;
        command.MaskId = _maskId;
    }

    private DepthStencilState GetMaskState(MaskMode mode, byte id)
    {
        if (mode == MaskMode.None)
            return DepthStencilState.None;

        if (_maskStates.TryGetValue((mode, id), out var state))
            return state;

        state = new DepthStencilState
        {
            DepthBufferEnable = false,
            DepthBufferWriteEnable = false,
            StencilEnable = true,
            ReferenceStencil = id,
            StencilFunction = mode switch
            {
                MaskMode.Inside => CompareFunction.Equal,
                MaskMode.Outside => CompareFunction.NotEqual,
                _ => CompareFunction.Always,
            },
            StencilPass = mode == MaskMode.Write ? StencilOperation.Replace : StencilOperation.Keep,
        };

        _maskStates[(mode, id)] = state;
        return state;
    }

    // A sprite writing a mask without a shader of its own gets one that drops its transparent texels.
    private ShaderInstance? GetMaskShader(ShaderInstance? own)
        => own?.Effect is not null ? own : _shaders.MaskInstance;
}
