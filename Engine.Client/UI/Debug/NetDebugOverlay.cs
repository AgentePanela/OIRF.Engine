using System;
using System.Collections.Generic;
using System.Linq;
using Apos.Shapes;
using Engine.Client.GameStates;
using Engine.Client.Graphics.Fonts;
using Engine.Shared.GameStates;
using Engine.Shared.IoC;
using Engine.Shared.Networking;
using Engine.Shared.Timing;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI.Debug;

/// <summary>
/// The replication stream, drawn: a traffic graph on top and a per-entity report under it. Toggled by the
/// netdebug command.
/// </summary>
public sealed class NetDebugOverlay : Overlay
{
    /// <summary>
    /// How many entity rows the report shows before it starts saying "and N more".
    /// </summary>
    public const int DefaultTopEntities = 20;

    private const float RefreshInterval = 0.25f;

    private static readonly Color PanelBg = new(0, 0, 0, 170);

    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ClientGameStateMetrics _metrics = default!;

    private readonly Label _headerLabel;
    private readonly NetGraphStrip _graph;
    private readonly List<EntityRow> _rows = new();
    private readonly Label _footerLabel;
    private readonly List<KeyValuePair<NetEntity, ClientGameStateMetrics.EntityTraffic>> _scratch = new(128);

    private float _refreshTimer;

    private readonly record struct EntityRow(BoxContainer Row, NetEntityStrip Strip, Label Name);

    public NetDebugOverlay(int topEntities = DefaultTopEntities)
    {
        IoCManager.ResolveDependencies(this);
        MouseFilter = MouseFilterMode.Ignore; // never blocks clicks to the game below
        StylesheetOverride = "EngineDefault";

        var panel = new BoxContainer
        {
            Orientation = Orientation.Vertical,
            Separation = 4,
            Background = PanelBg,
            Padding = new(8, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        AddChild(panel);

        //panel.AddChild(new Label { Text = "NET DEBUG", FontSize = 28f, Color = Color.White });

        _headerLabel = new Label { Color = Color.White, FontSize = 18f };
        panel.AddChild(_headerLabel);

        _graph = new NetGraphStrip(_metrics, _timing);
        panel.AddChild(_graph);

        panel.AddChild(new Separator());

        for (var i = 0; i < topEntities; i++)
        {
            var row = new BoxContainer { Orientation = Orientation.Horizontal, Separation = 6, Visible = false };
            var strip = new NetEntityStrip();
            var name = new Label { FontSize = 16f, Color = Color.White, AutoWrap = false, MinWidth = 220 };

            row.AddChild(strip);
            row.AddChild(name);
            panel.AddChild(row);

            _rows.Add(new EntityRow(row, strip, name));
        }

        _footerLabel = new Label { Color = Color.Gray, FontSize = 16f };
        panel.AddChild(_footerLabel);

        Refresh();
    }

    protected override void Update(float dt)
    {
        base.Update(dt);

        _refreshTimer += dt;
        if (_refreshTimer < RefreshInterval)
            return;

        _refreshTimer = 0f;
        Refresh();
    }

    private void Refresh()
    {
        if (!_net.IsClient || _net.MySession is not { } session)
        {
            _headerLabel.Text = "not connected";
            _footerLabel.Text = "";

            foreach (var row in _rows)
                row.Row.Visible = false;

            return;
        }

        var latest = _metrics.Latest;
        _headerLabel.Text =
            $"ping {session.Ping}ms | {FormatBytes(_metrics.BytesPerSecond(_timing.TickRate))}/s | " +
            $"tick {_metrics.LastAppliedTick} | queued {latest.Queued}" +
            (_metrics.AwaitingFull ? " | FULL STATE PENDING" : "");

        RefreshEntities();
    }

    private void RefreshEntities()
    {
        var newest = _metrics.LastAppliedTick;

        _scratch.Clear();
        foreach (var entry in _metrics.Entities)
            _scratch.Add(entry);

        // the point of the list is finding who is eating the bandwidth, so the busiest go first
        var ordered = _scratch
            .OrderByDescending(e => e.Value.Activity(newest))
            .ThenBy(e => e.Key.Id)
            .ToList();

        for (var i = 0; i < _rows.Count; i++)
        {
            if (i >= ordered.Count)
            {
                _rows[i].Row.Visible = false;
                continue;
            }

            var (netEnt, traffic) = ordered[i];

            _rows[i].Row.Visible = true;
            _rows[i].Strip.Set(traffic, newest);
            _rows[i].Name.Text = $"n{netEnt.Id} {(traffic.ProtoId.Length == 0 ? "(no proto)" : traffic.ProtoId)}" +
                $" {(traffic.Origin == EntityBlockKind.Global ? "G" : "R")}";
        }

        var hidden = ordered.Count - _rows.Count;
        _footerLabel.Text = hidden > 0 ? $"and {hidden} more" : $"{ordered.Count} replicated";
    }

    internal static string FormatBytes(double bytes)
    {
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):0.00}MB";

        return bytes >= 1024 ? $"{bytes / 1024.0:0.0}KB" : $"{bytes:0}B";
    }

    internal static void Fill(ShapeBatch sb, Rectangle rect, Color color)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        sb.FillRectangle(new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height),
            new ColorGradient(color).Resolve(rect), aaSize: 0f);
    }
}

/// <summary>
/// The traffic graph: state size per tick on top, ping and buffer depth underneath. One column per sample, drawn as
/// thin rectangles.
/// </summary>
internal sealed class NetGraphStrip : Control
{
    private const int PayloadHeight = 90;
    private const int LowerHeight = 40;

    // bytes per tick that each reference line sits at, at 60Hz: 8.19 / 33.6 / 56 / 128 Kbit/s
    private static readonly (double Bytes, string Label)[] References =
    [
        (8190 / 8.0 / 60.0, "8.19Kbit"),
        (33600 / 8.0 / 60.0, "33.6Kbit"),
        (56000 / 8.0 / 60.0, "56Kbit"),
        (128000 / 8.0 / 60.0, "128Kbit"),
    ];

    private static readonly Color BarColor = new(80, 255, 140, 220);
    private static readonly Color ReferenceColor = new(90, 90, 90, 200);
    private static readonly Color PingColor = new(110, 170, 255, 230);
    private static readonly Color QueueOkColor = new(80, 255, 140, 200);
    private static readonly Color QueueWarnColor = new(255, 220, 80, 200);
    private static readonly Color QueueBadColor = new(255, 90, 90, 200);

    private readonly ClientGameStateMetrics _metrics;
    private readonly IGameTiming _timing;

    protected internal override bool ClipsContent => true;

    public NetGraphStrip(ClientGameStateMetrics metrics, IGameTiming timing)
    {
        _metrics = metrics;
        _timing = timing;

        MinWidth = ClientGameStateMetrics.HistorySize;
        MinHeight = PayloadHeight + LowerHeight + 4;
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        base.DrawSelf(sb, fontManager, dt);

        var count = _metrics.SampleCount;
        var payloadBottom = Bounds.Y + PayloadHeight;

        foreach (var (bytes, label) in References)
        {
            var y = payloadBottom - (int)(LogScale(bytes) * PayloadHeight);
            if (y < Bounds.Y || y > payloadBottom)
                continue;

            NetDebugOverlay.Fill(sb, new Rectangle(Bounds.X, y, Bounds.Width, 1), ReferenceColor);

            var font = fontManager.Get(12f);
            sb.DrawString(font, label, new Vector2(Bounds.X + 2, y - 13), ReferenceColor);
        }

        if (count == 0)
            return;

        // oldest sample on the left, so a full history exactly fills the strip
        var first = Math.Max(0, count - Bounds.Width);
        for (var i = first; i < count; i++)
        {
            var sample = _metrics[i];
            var x = Bounds.X + (i - first);

            var height = (int)(LogScale(sample.Bytes) * PayloadHeight);
            NetDebugOverlay.Fill(sb, new Rectangle(x, payloadBottom - height, 1, height), BarColor);

            // 4ms per pixel, same scale the Robust netgraph uses
            var pingY = Bounds.Bottom - Math.Clamp(sample.Ping / 4, 0, LowerHeight - 1);
            NetDebugOverlay.Fill(sb, new Rectangle(x, pingY, 1, 1), PingColor);

            if (sample.Queued <= 0)
                continue;

            var queueColor = sample.Queued switch
            {
                1 => QueueOkColor,
                2 or 3 => QueueWarnColor,
                _ => QueueBadColor,
            };
            var queueHeight = Math.Clamp(sample.Queued * 3, 1, LowerHeight);
            NetDebugOverlay.Fill(sb, new Rectangle(x, Bounds.Bottom - queueHeight, 1, queueHeight), queueColor);
        }
    }

    // a full state is a couple hundred bytes and a fat one is tens of kilobytes, so a linear axis shows nothing
    private static double LogScale(double bytes)
        => bytes <= 1.0 ? 0.0 : Math.Clamp(Math.Log(bytes) / Math.Log(64 * 1024), 0.0, 1.0);
}

/// <summary>
/// One entity's last <see cref="ClientGameStateMetrics.EntityHistorySize"/> ticks, one pixel column per tick.
/// </summary>
internal sealed class NetEntityStrip : Control
{
    private static readonly Color Background = new(40, 40, 40, 220);
    private static readonly Color DataColor = new(80, 255, 140, 230);
    private static readonly Color EnterColor = new(80, 230, 255, 230);
    private static readonly Color LeaveColor = new(255, 170, 60, 230);

    private ClientGameStateMetrics.EntityTraffic? _traffic;
    private GameTick _newest;

    public NetEntityStrip()
    {
        MinWidth = ClientGameStateMetrics.EntityHistorySize;
        MinHeight = 12;
    }

    public void Set(ClientGameStateMetrics.EntityTraffic traffic, GameTick newest)
    {
        _traffic = traffic;
        _newest = newest;
    }

    protected override void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        base.DrawSelf(sb, fontManager, dt);

        NetDebugOverlay.Fill(sb, Bounds, Background);

        if (_traffic is null)
            return;

        var width = ClientGameStateMetrics.EntityHistorySize;
        for (var i = 0; i < width; i++)
        {
            // oldest on the left
            var tick = new GameTick(_newest.Value - (uint)(width - 1 - i));
            var color = _traffic.At(tick) switch
            {
                ClientGameStateMetrics.NetEntState.Data => DataColor,
                ClientGameStateMetrics.NetEntState.Enter => EnterColor,
                ClientGameStateMetrics.NetEntState.Leave => LeaveColor,
                _ => (Color?)null,
            };

            if (color is not null)
                NetDebugOverlay.Fill(sb, new Rectangle(Bounds.X + i, Bounds.Y, 1, Bounds.Height), color.Value);
        }
    }
}
