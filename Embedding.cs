// TimeLight Map: an in-game 3D picture-in-picture map for MeitouClient.
// Copyright (C) 2026 TimeLight Map contributors
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable
using System;
using System.Numerics;
using Meitou.Game.Rendering.Gpu;

namespace TimeLightMap;

/// <summary>Shares the live map with one host UI on Meitou's render thread.</summary>
public static class Embedding
{
    public const int ApiVersion = 1;
    private const float MaxWorldSpan = 50_000;
    private const int MaxTextureSide = 2048;
    private static bool rendererReady;
    private static string? owner;
    private static FrameRequest? request;
    private static Frame? frame;

    internal readonly record struct FrameRequest(Vector4 WorldBounds, int Width, int Height);
    private readonly record struct Frame(Vector4 WorldBounds, GpuTexture Texture, SampledTexture Sampled, bool FlipV);

    internal static bool EmbeddedMode => owner is not null;
    internal static FrameRequest? PendingFrame => request;

    public static bool TrySetEmbeddedMode(string consumer, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(consumer)) return false;
        if (!enabled)
        {
            if (owner != consumer) return false;
            ClearRequest(consumer);
            owner = null;
            return true;
        }
        if (!rendererReady || owner is not null && owner != consumer) return false;
        owner = consumer;
        return true;
    }

    public static bool TryRequestFrame(string consumer, Vector4 worldBounds, int width, int height)
    {
        if (!rendererReady || owner is null || owner != consumer) return false;
        float spanX = worldBounds.Z - worldBounds.X, spanZ = worldBounds.W - worldBounds.Y;
        if (!(spanX > 0 && spanX <= MaxWorldSpan && spanZ > 0 && spanZ <= MaxWorldSpan) || width < 16 || height < 16)
        {
            ClearRequest(consumer);
            return false;
        }
        float scale = Math.Min(1f, MaxTextureSide / (float)Math.Max(width, height));
        request = new(worldBounds, Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
        return true;
    }

    public static bool TryGetFrame(string consumer, out Vector4 worldBounds, out GpuTexture texture,
        out SampledTexture sampled, out bool flipV)
    {
        if (rendererReady && owner is not null && owner == consumer && frame is {} ready)
        {
            worldBounds = ready.WorldBounds;
            texture = ready.Texture;
            sampled = ready.Sampled;
            flipV = ready.FlipV;
            return true;
        }
        worldBounds = default;
        texture = null!;
        sampled = default;
        flipV = false;
        return false;
    }

    public static void ClearRequest(string consumer)
    {
        if (owner is null || owner != consumer) return;
        request = null;
        frame = null;
    }

    internal static void SetRendererReady(bool ready)
    {
        rendererReady = ready;
        if (ready) return;
        request = null;
        frame = null;
    }

    internal static void Publish(FrameRequest rendered, GpuTexture texture, SampledTexture sampled, bool flipV)
    {
        if (rendererReady && request is not null)
            frame = new(rendered.WorldBounds, texture, sampled, flipV);
    }

    internal static void InvalidateFrame() => frame = null;
}
