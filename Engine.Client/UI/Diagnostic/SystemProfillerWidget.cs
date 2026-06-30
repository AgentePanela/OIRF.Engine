using Engine.Client.Debug.Diagnostics;

using FontStashSharp;
using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using Myra.Graphics2D;
using System;
using System.Collections.Generic;
using Engine.Shared.Debug.Diagnostics;


namespace Engine.Client.UI.Debug;

// i asked claude to make this one, it would be so painfull create one from zero </3

/// <summary>
/// Draws a horizontal bar chart of the top N systems by Update+Draw time.
/// Placed in the top-right corner of the debug overlay.
/// </summary>
public sealed class SystemProfilerWidget : Widget
{
    private const int PanelWidth = 300;
    private const int HeaderHeight = 32;
    private const int SectionTitleHeight = 24;
    private const int BarHeight = 15;
    private const int RowHeight = BarHeight + 8;
    private const int LabelWidth = 100;
    private const int BarMaxWidth = 110;
    private const int PaddingX = 8;
    private const int PaddingY = 8;
    private const int SectionGap = 12;
 
    private static readonly Color HeaderColor = new(220, 220, 220, 255);
    private static readonly Color SectionTitleColor = new(180, 180, 180, 255);
    private static readonly Color UpdateBarColor = new(80, 180, 255, 220);
    private static readonly Color DrawBarColor = new(80, 255, 140, 220);
    private static readonly Color BarBackColor = new(50, 50, 50, 200);
    private static readonly Color LabelColor = new(210, 210, 210, 255);
    private static readonly Color UpdateValueColor = new(140, 210, 255, 255);
    private static readonly Color DrawValueColor = new(140, 255, 170, 255);
 
    // Pre-baked render entry — built on Tick, read-only on Render.
    private readonly struct RenderEntry
    {
        public readonly string Label;
        public readonly string Value;
        public readonly float BarFraction; // 0..1
 
        public RenderEntry(string label, string value, float barFraction)
        {
            Label = label;
            Value = value;
            BarFraction = barFraction;
        }
    }
 
    private readonly SpriteFontBase _font;
    private readonly SystemsProfiler _profiler;
 
    // Two fixed-capacity lists reused across ticks — no re-allocation after warmup.
    private readonly List<RenderEntry> _updateEntries = new(10);
    private readonly List<RenderEntry> _drawEntries = new(10);
 
    // Scratch list reused in Tick to avoid allocating on every refresh.
    private readonly List<SystemSnapshot> _scratch = new(64);
 
    private float _refreshTimer;
    private const float RefreshInterval = 0.25f;
 
    public SystemProfilerWidget(SpriteFontBase font, SystemsProfiler profiler)
    {
        _font = font;
        _profiler = profiler;
        Width = PanelWidth;
        UpdateHeight();
    }
 
    public override void InternalRender(RenderContext context)
    {
        var b = ActualBounds;
        int x = b.X + PaddingX;
        int y = b.Y + PaddingY;
 
        context.DrawString(_font, "SYSTEMS PROFILER", new Vector2(x, y), HeaderColor);
        y += HeaderHeight;
 
        y = DrawSection(context, x, y, "UPDATE", _updateEntries, UpdateBarColor, UpdateValueColor);
        y += SectionGap;
        DrawSection(context, x, y, "DRAW", _drawEntries, DrawBarColor, DrawValueColor);
    }
 
    private int DrawSection(RenderContext context, int x, int y, string title,
        List<RenderEntry> entries, Color barColor, Color valueColor)
    {
        context.DrawString(_font, title, new Vector2(x, y), SectionTitleColor);
        y += SectionTitleHeight;
 
        if (entries.Count == 0)
        {
            context.DrawString(_font, "no data yet...", new Vector2(x, y + 2), LabelColor);
            return y + RowHeight;
        }
 
        int barAreaX = x + LabelWidth;
        int valueX = barAreaX + BarMaxWidth + 6;
 
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
 
            context.DrawString(_font, e.Label, new Vector2(x, y + 1), LabelColor);
 
            context.FillRectangle(new Rectangle(barAreaX, y, BarMaxWidth, BarHeight), BarBackColor);
 
            int barW = (int)(e.BarFraction * BarMaxWidth);
            if (barW > 0)
                context.FillRectangle(new Rectangle(barAreaX, y, barW, BarHeight), barColor);
 
            context.DrawString(_font, e.Value, new Vector2(valueX, y + 1), valueColor);
 
            y += RowHeight;
        }
 
        return y;
    }
 
    public void Tick(float dt)
    {
        _refreshTimer += dt;
        if (_refreshTimer < RefreshInterval)
            return;
 
        _refreshTimer = 0f;
 
        _scratch.Clear();
        foreach (var s in _profiler.GetAll())
            _scratch.Add(s);
 
        BakeEntries(_scratch, s => s.UpdateMs, _updateEntries);
        BakeEntries(_scratch, s => s.DrawMs, _drawEntries);
 
        UpdateHeight();
    }
 
    private static void BakeEntries(List<SystemSnapshot> source,
        Func<SystemSnapshot, double> getValue, List<RenderEntry> dest)
    {
        dest.Clear();
 
        // Partial insertion sort for top-10 — avoids allocating a sorted copy.
        const int max = 10;
        int take = Math.Min(max, source.Count);
 
        // Find top `take` by value using a simple selection approach.
        // For N=10 and typical system counts (< 50) this is fine.
        Span<int> topIdx = stackalloc int[max];
        Span<bool> used = stackalloc bool[source.Count];
 
        for (int rank = 0; rank < take; rank++)
        {
            double best = -1;
            int bestIdx = -1;
            for (int i = 0; i < source.Count; i++)
            {
                if (used[i]) continue;
                double v = getValue(source[i]);
                if (v > best) { best = v; bestIdx = i; }
            }
            if (bestIdx < 0) break;
            topIdx[rank] = bestIdx;
            used[bestIdx] = true;
        }
 
        double maxMs = 0.001;
        for (int i = 0; i < take; i++)
            maxMs = Math.Max(maxMs, getValue(source[topIdx[i]]));
 
        for (int i = 0; i < take; i++)
        {
            var snap = source[topIdx[i]];
            double ms = getValue(snap);
            dest.Add(new RenderEntry(
                TruncateName(snap.Name, 14),
                FormatTime(ms),
                (float)(ms / maxMs)));
        }
    }
 
    private void UpdateHeight()
    {
        int updateRows = Math.Max(1, _updateEntries.Count);
        int drawRows = Math.Max(1, _drawEntries.Count);
 
        Height = PaddingY * 2
               + HeaderHeight
               + SectionTitleHeight + updateRows * RowHeight
               + SectionGap
               + SectionTitleHeight + drawRows * RowHeight
               + PaddingY;
    }
 
    private static string TruncateName(string name, int maxChars)
    {
        name = name.Replace("System", "Sys").Replace("Manager", "Mgr");
        return name.Length <= maxChars ? name : name[..maxChars];
    }
 
    private static string FormatTime(double ms)
    {
        if (ms >= 1000.0) return $"{ms / 1000.0:0.00}s";
        if (ms >= 1.0) return $"{ms:0.00}ms";
        if (ms >= 0.001) return $"{ms * 1000.0:0.0}µs";
        return "0µs";
    }
}