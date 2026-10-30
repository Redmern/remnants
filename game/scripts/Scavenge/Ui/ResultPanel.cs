using System;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// End-of-run summary from Core's <see cref="ExtractionResult"/>: who came home (health, hurt parts as body-icon
/// chips, evacuated), recruits, the dead and the loot, in the after-action style of the colony report.
/// </summary>
public partial class ResultPanel : Overlay
{
    private readonly Label _title = Widgets.Label("", "Title");
    private readonly VBoxContainer _body = Widgets.Column(8);

    public event Action? ContinuePressed;

    public ResultPanel() : base(620)
    {
        Visible = false;
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        Body.AddChild(_title);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 320) };
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_body);
        Body.AddChild(scroll);
        var ok = Widgets.IconButton("stat_check", "Continue", "Back to the colony", () => ContinuePressed?.Invoke(), variation: "Primary");
        ok.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        ok.CustomMinimumSize = new Vector2(180, 38);
        Body.AddChild(ok);
    }

    /// <summary>Esc does not dismiss the summary: Continue hands the result back.</summary>
    public override void Close() { }

    public void Present(ScavengeData data, ExtractionResult result)
    {
        var wiped = result.Returned.Count == 0;
        _title.Text = wiped ? "NO ONE CAME BACK" : result.Deaths.Count > 0 ? "EXTRACTION — WITH LOSSES" : "EXTRACTION COMPLETE";
        _title.AddThemeColorOverride("font_color", wiped ? Palette.Danger : result.Deaths.Count > 0 ? Palette.Warn : Palette.Good);
        Widgets.Clear(_body);
        if (!result.Complete) _body.AddChild(Widgets.Label("Left early: the run is not finished.", "", Palette.Danger));

        _body.AddChild(Widgets.SectionHeader("Returned", "stat_check"));
        if (result.Returned.Count == 0) _body.AddChild(Widgets.Label("Nobody.", "Muted"));
        foreach (var r in result.Returned)
        {
            var card = new PanelContainer { ThemeTypeVariation = "Card" };
            var col = Widgets.Column(4);
            card.AddChild(col);
            var top = Widgets.Row(8);
            var name = Widgets.Label(r.Survivor.Name, "", size: 16);
            name.AddThemeFontOverride("font", Fonts.BodyBold);
            top.AddChild(name);
            if (r.Evacuated) top.AddChild(Widgets.Chip(Icons.Get("ui_extract", 14), "evacuated", Palette.Warn, "Pulled out by an evac beacon.", Palette.Warn));
            top.AddChild(Widgets.Spacer());
            var hp = new StatBar("stat_hp", 140);
            hp.Set(r.Survivor.Health / Survivor.MaxHealth, $"{r.Survivor.Health:0}");
            top.AddChild(hp);
            col.AddChild(top);
            var hurt = r.Survivor.Body.Parts.Where(kv => kv.Value.Condition is PartCondition.Injured or PartCondition.Missing).ToList();
            if (hurt.Count > 0)
            {
                var flow = Flow();
                foreach (var (part, state) in hurt)
                {
                    var lost = state.Condition == PartCondition.Missing;
                    flow.AddChild(Widgets.Chip(Icons.BodyPart(part.ToString(), 14),
                        $"{ScavengeText.Part(part)} {(lost ? "lost" : Fmt.Percent(state.Severity))}", lost ? Palette.Danger : Palette.Warn, "", lost ? Palette.Danger : Palette.Warn));
                }
                col.AddChild(flow);
            }
            _body.AddChild(card);
        }

        if (result.Recruits.Count > 0)
        {
            _body.AddChild(Widgets.SectionHeader("Recruits", "stat_recruit"));
            var flow = Flow();
            foreach (var r in result.Recruits)
                flow.AddChild(Widgets.Chip(Icons.Get("stat_recruit", 14), r.Survivor.Name, Palette.Good, "Joins the colony", Palette.Good));
            _body.AddChild(flow);
        }
        if (result.Deaths.Count > 0)
        {
            _body.AddChild(Widgets.SectionHeader("Lost", "stat_dead"));
            var flow = Flow();
            foreach (var d in result.Deaths)
                flow.AddChild(Widgets.Chip(Icons.Get("stat_dead", 14), d.Name, Palette.Danger, "Did not come back", Palette.Danger));
            _body.AddChild(flow);
        }

        _body.AddChild(Widgets.SectionHeader("Loot brought home", "stat_loot"));
        var loot = Flow();
        if (result.Loot.Count == 0) loot.AddChild(Widgets.Label("Nothing.", "Muted"));
        foreach (var (id, count) in result.Loot)
        {
            var def = data.Items.GetValueOrDefault(id);
            loot.AddChild(Widgets.Chip(def is null ? Icons.Get("item_generic", 14) : Icons.Item(def.DefinedId, def.Kind.ToString(), 14),
                $"{ScavengeText.Item(data, id)} ×{count}", null, def?.Kind.ToString() ?? ""));
        }
        _body.AddChild(loot);
        Open();
    }

    private static HFlowContainer Flow()
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        return flow;
    }
}
