using Godot;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>
/// One colour per side (allies and other players' parties get their own), used by unit rings, world labels, turn-order
/// chips and portraits so they all agree.
/// </summary>
public static class SidePalette
{
    public static readonly Color Mine = Hud.Palette.Amber;
    public static readonly Color Recruit = Hud.Palette.Good;
    public static readonly Color Enemy = Hud.Palette.Danger;
    /// <summary>The AI Network's units: the one place cyan is used.</summary>
    public static readonly Color Linked = Hud.Palette.Network;
    /// <summary>An allied player's party (co-op): same side, their orders.</summary>
    public static readonly Color Ally = Hud.Palette.Ally;
    /// <summary>Another player's party (multiplayer): hostile, but not the AI.</summary>
    public static readonly Color OtherPlayer = new(0.75f, 0.45f, 0.95f);
    public static readonly Color Neutral = new(0.85f, 0.85f, 0.85f);
    public static readonly Color Unknown = new(0.45f, 0.43f, 0.4f);

    public static Color For(ScavengePresenter presenter, Unit u) => u switch
    {
        { Kind: UnitKind.Enemy } => u.EnemyDefId?.StartsWith("linked") == true ? Linked : Enemy,
        _ when presenter.IsMine(u) => u.FollowingId is null ? Mine : Recruit,
        { Side: Unit.NeutralSide } => Neutral,
        _ when presenter.IsAlly(u) => u.FollowingId is null ? Ally : Ally.Darkened(0.25f),
        { PlayerId: not null } => OtherPlayer,
        _ => Enemy,
    };
}
