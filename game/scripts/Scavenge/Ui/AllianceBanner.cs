using System.Linq;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The alliance banner under the turn bar: an open offer to the local player (answer on your turn: Y / U), a break
/// announced on the team (with the round it takes effect), and for a few seconds the latest alliance news (allied,
/// declined, broken). Ally green for offers and news, warning amber for a coming break. Hidden otherwise.
/// </summary>
public partial class AllianceBanner : PanelContainer
{
    private const double NewsSeconds = 6;

    private readonly ScavengePresenter _presenter;
    private readonly TextureRect _icon = Icons.Rect(Icons.Get("stat_ally", 22), 22, Palette.Ally);
    private readonly Label _title = Widgets.Label("", "Heading");
    private readonly Label _text = Widgets.Label("", "Small");
    private string? _news;
    private double _newsLeft;

    public AllianceBanner(ScavengePresenter presenter)
    {
        _presenter = presenter;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var row = Widgets.Row(10);
        row.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(row);
        row.AddChild(_icon);
        var col = Widgets.Column(0);
        col.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(_title);
        col.AddChild(_text);
        row.AddChild(col);
        presenter.AllianceNews += text =>
        {
            _news = text;
            _newsLeft = NewsSeconds;
            Refresh();
        };
    }

    public override void _Process(double delta)
    {
        if (_newsLeft <= 0) return;
        _newsLeft -= delta;
        if (_newsLeft <= 0)
        {
            _news = null;
            Refresh();
        }
    }

    public void Refresh()
    {
        var state = _presenter.State;
        var offer = _presenter.OffersToMe.FirstOrDefault();
        var leaving = _presenter.TeamBreaks.OrderBy(b => b.EffectiveRound).FirstOrDefault();
        string title, text;
        Color color;
        if (offer is not null)
        {
            (title, color) = ($"{_presenter.PlayerName(offer.FromPlayerId).ToUpperInvariant()} OFFERS AN ALLIANCE", Palette.Ally);
            var accept = Hotkeys.KeyName("scav_ally");
            var decline = Hotkeys.KeyName("scav_decline");
            text = $"Shared sight, no friendly fire. Answer on your turn: {accept} accept · {decline} decline. Open until round {offer.Round + _presenter.Sim.Data.Rules.Coop.ProposalRounds - 1}.";
        }
        else if (leaving is not null)
        {
            var who = leaving.PlayerId == _presenter.PlayerId ? "YOU LEAVE" : $"{_presenter.PlayerName(leaving.PlayerId).ToUpperInvariant()} LEAVES";
            (title, color) = ($"{who} THE ALLIANCE", Palette.Warn);
            var rounds = leaving.EffectiveRound - state.Round;
            text = $"When round {leaving.EffectiveRound} starts ({rounds} round{(rounds == 1 ? "" : "s")}): shared sight ends, fire is open again.";
        }
        else if (_news is not null)
        {
            (title, color, text) = ("ALLIANCE", Palette.Ally, _news);
        }
        else
        {
            Visible = false;
            return;
        }
        _title.Text = title;
        _title.AddThemeColorOverride("font_color", color);
        _text.Text = text;
        _icon.Modulate = color;
        var box = HudTheme.Box(Palette.Panel, color, 1, radius: 3, padX: 14, padY: 8);
        box.BorderWidthLeft = 4;
        AddThemeStyleboxOverride("panel", box);
        Visible = true;
    }
}
