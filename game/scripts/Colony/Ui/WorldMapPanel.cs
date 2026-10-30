using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Game.Hud;
using Remnants.Core.Colony;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The world map: a stylized top-down chart of the zones the colony knows (rumored zones as hollow "?" markers,
/// scouted ones coloured by threat and sized by reward tier, a cyan ring for Linked presence, a dimmed arc for
/// depletion), dashed roads from the colony labelled with travel days, region names, and the party on the road.
/// Clicking a marker shows its card; "Plan expedition" hands the zone to the <see cref="ExpeditionPanel"/>, which owns
/// the party picker and launch. Reads Core (<c>ColonySim.WorldZones</c>); no rules here.
/// </summary>
public partial class WorldMapPanel : Control
{
    private static readonly Color Ink = new(0.82f, 0.78f, 0.68f);

    private readonly ColonySession _session;
    private readonly WorldMapCanvas _canvas;
    private readonly PanelContainer _card = new() { CustomMinimumSize = new Vector2(380, 0) };
    private readonly Label _cardTitle = Widgets.Label("", "Display", size: 24);
    private readonly Label _cardRegion = Ui.Label(size: 13, color: Ui.Muted);
    private readonly RichTextLabel _cardBody = Ui.Rich();
    private readonly Button _plan;
    private string? _selected;

    /// <summary>"Plan expedition" for a zone: the screen opens the expedition planner on it.</summary>
    public event Action<string>? PlanPressed;

    public WorldMapPanel(ColonySession session)
    {
        _session = session;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _canvas = new WorldMapCanvas(this) { MouseFilter = MouseFilterEnum.Stop };
        _canvas.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_canvas);

        var header = new HBoxContainer { Position = new Vector2(24, 18) };
        var title = Widgets.Label("WORLD MAP", "Title", Ink);
        header.AddChild(title);
        AddChild(header);

        var close = Widgets.IconButton("ui_close", "Close", "Close the map", Close, Hotkeys.KeyName("pause_menu"));
        close.SetAnchorsPreset(LayoutPreset.TopRight);
        close.Position = new Vector2(-134, 18);
        AddChild(close);

        _card.SetAnchorsPreset(LayoutPreset.RightWide);
        _card.OffsetLeft = -404;
        _card.OffsetRight = -24;
        _card.OffsetTop = 70;
        _card.OffsetBottom = -24;
        AddChild(_card);
        var box = Ui.Panel(_card, 8);
        box.AddChild(_cardTitle);
        box.AddChild(_cardRegion);
        _cardBody.CustomMinimumSize = new Vector2(340, 0);
        box.AddChild(_cardBody);
        _plan = Widgets.IconButton("ui_expedition", "Plan expedition", "Open the expedition planner on this zone", () =>
        {
            if (_selected is not { } id) return;
            Close();
            PlanPressed?.Invoke(id);
        });
        _plan.ThemeTypeVariation = "Primary";
        box.AddChild(_plan);
        box.AddChild(Legend());
    }

    public void Open(string? zoneId = null)
    {
        Visible = true;
        _selected = zoneId ?? _session.Sim.State.Expedition?.ZoneId ?? _selected
            ?? _session.Sim.WorldZones().FirstOrDefault(z => z.Intel != ZoneIntel.Unknown)?.Zone.Id;
        Refresh();
    }

    public void Close() => Visible = false;

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    public void Refresh()
    {
        if (!Visible) return;
        _canvas.QueueRedraw();
        var sim = _session.Sim;
        if (_selected is not { } id || sim.ZoneInfo(id) is not { Intel: not ZoneIntel.Unknown } info)
        {
            _cardTitle.Text = "Choose a zone";
            _cardRegion.Text = "";
            _cardBody.Text = "Click a marker on the map.";
            _plan.Disabled = true;
            return;
        }
        _cardTitle.Text = info.Zone.Name;
        _cardRegion.Text = info.Zone.Region.ToUpperInvariant() + (info.Visits > 0 ? $"  ·  visited {info.Visits}×" : "");
        var body = ColonyText.ZoneSummary(sim, info);
        if (info.Intel == ZoneIntel.Scouted && !string.IsNullOrEmpty(info.Zone.Description)) body = $"[i]{info.Zone.Description}[/i]\n\n{body}";
        _cardBody.Text = body;
        _plan.Disabled = sim.State.Expedition is not null;
        _plan.Text = sim.State.Expedition is null ? "Plan expedition" : "A party is already out";
    }

    private static Control Legend()
    {
        var legend = Ui.Rich();
        legend.Text =
            $"\n[color=#{Ui.Hex(Ui.Muted)}]LEGEND[/color]\n" +
            $"[color=#{ColonyText.ThreatColor(1).ToHtml(false)}]●[/color] threat 1  " +
            $"[color=#{ColonyText.ThreatColor(2).ToHtml(false)}]●[/color] 2  " +
            $"[color=#{ColonyText.ThreatColor(3).ToHtml(false)}]●[/color] 3  " +
            $"[color=#{ColonyText.ThreatColor(4).ToHtml(false)}]●[/color] 4\n" +
            "bigger marker = richer loot · [color=#4dd9f2]○[/color] Linked presence · ? rumored, not scouted";
        return legend;
    }

    internal ColonySim Sim => _session.Sim;

    internal string? Selected => _selected;

    internal void Select(string zoneId)
    {
        _selected = zoneId;
        Refresh();
    }
}

/// <summary>The world map's drawing surface; normalized zone positions are mapped into the area left of the card.</summary>
public partial class WorldMapCanvas : Control
{
    private readonly WorldMapPanel _owner;

    public WorldMapCanvas() : this(null!) { }

    public WorldMapCanvas(WorldMapPanel owner) => _owner = owner;

    private static readonly Color Sea = new(0.06f, 0.06f, 0.055f);
    private static readonly Color Land = new(0.15f, 0.14f, 0.12f);
    private static readonly Color LandEdge = new(0.3f, 0.26f, 0.2f);
    private static readonly Color River = new(0.17f, 0.21f, 0.22f);
    private static readonly Color Ink = new(0.82f, 0.78f, 0.68f);
    private static readonly Color Network = Palette.Network;

    private const float MarkerBase = 11f;

    private Rect2 Area()
    {
        var size = Size;
        return new Rect2(60, 90, Math.Max(200, size.X - 404 - 24 - 90), Math.Max(200, size.Y - 90 - 60));
    }

    private Vector2 At(double[] pos)
    {
        var area = Area();
        return area.Position + new Vector2((float)pos[0] * area.Size.X, (float)pos[1] * area.Size.Y);
    }

    private static float Radius(ZoneDef zone) => MarkerBase + zone.LootTier * 3.5f;

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        var hit = _owner.Sim.WorldZones()
            .Where(z => z.Intel != ZoneIntel.Unknown)
            .Select(z => (z.Zone.Id, Distance: At(z.Zone.MapPos).DistanceTo(click.Position) - Radius(z.Zone)))
            .Where(z => z.Distance < 10)
            .OrderBy(z => z.Distance)
            .FirstOrDefault();
        if (hit.Id is not null) _owner.Select(hit.Id);
        AcceptEvent();
    }

    public override void _Draw()
    {
        var sim = _owner.Sim;
        var data = sim.Scavenge!;
        var font = GetThemeDefaultFont();
        var area = Area();

        DrawRect(new Rect2(Vector2.Zero, Size), Sea);
        DrawLand(area);
        DrawGrid(area);

        var zones = sim.WorldZones().Where(z => z.Intel != ZoneIntel.Unknown).ToList();
        var colony = At(data.Rules.World.ColonyPos);

        // Region names behind everything else, at the middle of the region's known zones.
        foreach (var region in zones.GroupBy(z => z.Zone.Region).Where(g => g.Key.Length > 0))
        {
            // Above the region's highest marker, centred over its markers.
            var points = region.Select(z => At(z.Zone.MapPos)).ToList();
            var x = (points.Min(p => p.X) + points.Max(p => p.X)) / 2;
            var y = points.Min(p => p.Y) - 52;
            var text = string.Join(" ", region.Key.ToUpperInvariant().ToCharArray());
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 15).X;
            DrawString(font, new Vector2(x - width / 2, y), text, HorizontalAlignment.Left, -1, 15, new Color(Ink, 0.35f));
        }

        // Roads: dashed from the colony, labelled with the travel time.
        foreach (var info in zones)
        {
            var to = At(info.Zone.MapPos);
            if (info.Zone.TravelDays <= 0 && to.DistanceTo(colony) < 40) continue;
            var color = new Color(ColonyText.ThreatColor(info.Zone.Threat), info.Intel == ZoneIntel.Scouted ? 0.55f : 0.3f);
            DrawDashedLine(colony, to, color, 2f, 9f);
            if (info.Zone.TravelDays <= 0) continue;
            // The label sits beside the road, a little past halfway.
            var along = colony.Lerp(to, 0.58f);
            var side = (to - colony).Normalized().Orthogonal() * 12;
            var label = $"{info.Zone.TravelDays:0.#}d";
            DrawString(font, along + side + new Vector2(-6, 4), label, HorizontalAlignment.Left, -1, 13, new Color(Ink, 0.8f));
        }

        DrawColony(colony, font);
        foreach (var info in zones) DrawZone(info, font, info.Zone.Id == _owner.Selected);
        DrawParty(sim, data, colony);
    }

    /// <summary>A fixed, hand-shaped landmass with a river through it: the same picture every time.</summary>
    private void DrawLand(Rect2 area)
    {
        Vector2 P(float x, float y) => area.Position + new Vector2(x * area.Size.X, y * area.Size.Y);
        var outline = new[]
        {
            P(-0.04f, 0.08f), P(0.18f, -0.03f), P(0.42f, 0.04f), P(0.63f, -0.02f), P(0.86f, 0.0f), P(1.04f, 0.07f),
            P(1.02f, 0.42f), P(1.05f, 0.78f), P(0.9f, 1.03f), P(0.6f, 0.96f), P(0.38f, 1.05f), P(0.1f, 0.98f),
            P(-0.05f, 0.82f), P(-0.02f, 0.45f),
        };
        DrawColoredPolygon(outline, Land);
        DrawPolyline([.. outline, outline[0]], LandEdge, 3f, true);

        var river = new[] { P(0.0f, 0.3f), P(0.14f, 0.36f), P(0.25f, 0.44f), P(0.36f, 0.4f), P(0.5f, 0.62f), P(0.58f, 0.78f), P(0.7f, 0.86f), P(0.82f, 1.0f) };
        DrawPolyline(river, River, 6f, true);
        var lake = new List<Vector2>();
        for (var i = 0; i < 20; i++)
        {
            var a = i / 20f * Mathf.Tau;
            lake.Add(P(0.3f, 0.27f) + new Vector2(Mathf.Cos(a) * 38, Mathf.Sin(a) * 22));
        }
        DrawColoredPolygon([.. lake], River);
    }

    private void DrawGrid(Rect2 area)
    {
        var color = new Color(Ink, 0.05f);
        for (var i = 0; i <= 10; i++)
        {
            var x = area.Position.X + area.Size.X * i / 10f;
            var y = area.Position.Y + area.Size.Y * i / 10f;
            DrawLine(new Vector2(x, area.Position.Y - 20), new Vector2(x, area.End.Y + 20), color, 1f);
            DrawLine(new Vector2(area.Position.X - 20, y), new Vector2(area.End.X + 20, y), color, 1f);
        }
    }

    private void DrawColony(Vector2 at, Font font)
    {
        DrawCircle(at, 20, new Color(Ink, 0.12f));
        DrawRect(new Rect2(at + new Vector2(-9, -4), new Vector2(18, 12)), Ink);
        DrawColoredPolygon([at + new Vector2(-12, -4), at + new Vector2(0, -15), at + new Vector2(12, -4)], Ink);
        DrawString(font, at + new Vector2(-24, 30), "COLONY", HorizontalAlignment.Left, -1, 13, Ink);
    }

    private void DrawZone(ZoneInfo info, Font font, bool selected)
    {
        var zone = info.Zone;
        var at = At(zone.MapPos);
        var r = Radius(zone);
        var color = ColonyText.ThreatColor(zone.Threat);
        if (selected) DrawArc(at, r + 9, 0, Mathf.Tau, 40, Ink, 2f, true);

        if (info.Intel == ZoneIntel.Rumored)
        {
            DrawCircle(at, r, new Color(Sea, 0.85f));
            DrawArc(at, r, 0, Mathf.Tau, 32, new Color(Ink, 0.6f), 2f, true);
            DrawString(font, at + new Vector2(-5, 7), "?", HorizontalAlignment.Left, -1, 20, Ink);
        }
        else
        {
            DrawCircle(at, r, color.Darkened(0.35f));
            DrawCircle(at, r - 4, color);
            // Depletion: a dark wedge eating into the marker.
            if (info.Depletion > 0.01)
                DrawArc(at, r - 2, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (float)info.Depletion, 32, new Color(0, 0, 0, 0.55f), 4f, true);
            var tier = new string('◆', zone.LootTier);
            var w = font.GetStringSize(tier, HorizontalAlignment.Left, -1, 11).X;
            DrawString(font, at + new Vector2(-w / 2, 4), tier, HorizontalAlignment.Left, -1, 11, new Color(0.1f, 0.1f, 0.1f));
            if (info.Linked != LinkedPresence.None)
            {
                var rings = (int)info.Linked;
                for (var i = 0; i < rings; i++)
                    DrawArc(at, r + 4 + i * 4, 0, Mathf.Tau, 40, new Color(Network, 0.85f - i * 0.2f), 1.5f, true);
            }
        }

        var name = zone.Name;
        var width = font.GetStringSize(name, HorizontalAlignment.Left, -1, 14).X;
        DrawString(font, at + new Vector2(-width / 2, r + 24), name, HorizontalAlignment.Left, -1, 14, Ink);
    }

    /// <summary>The party as a marker on its road: walking out, in the zone, or walking home.</summary>
    private void DrawParty(ColonySim sim, ScavengeData data, Vector2 colony)
    {
        if (sim.State.Expedition is not { } e || !data.Zones.TryGetValue(e.ZoneId, out var zone)) return;
        var target = At(zone.MapPos);
        var tick = sim.State.Tick;
        var progress = e.PhaseAt(tick) switch
        {
            ExpeditionPhase.Outbound => e.TravelTicks > 0 ? (tick - e.LaunchedTick) / (float)e.TravelTicks : 1f,
            ExpeditionPhase.Returning => e.TravelTicks > 0 ? 1f - (tick - (e.ReturnTick - e.TravelTicks)) / (float)e.TravelTicks : 0f,
            _ => 1f,
        };
        var at = colony.Lerp(target, Mathf.Clamp(progress, 0, 1)) + new Vector2(0, -Radius(zone) - 10) * (progress >= 1 ? 1 : 0);
        DrawCircle(at, 7, new Color(0.95f, 0.9f, 0.7f));
        DrawArc(at, 7, 0, Mathf.Tau, 20, new Color(0.1f, 0.1f, 0.1f), 2f, true);
    }
}
