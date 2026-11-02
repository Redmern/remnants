using System.Collections.Generic;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>
/// What the player's colony knows of enemy types (<c>ColonyState.Bestiary</c>), handed to the scavenge screen when a
/// run starts from the colony; empty for a debug or a menu-started run. The run's own encounters are added on top
/// (<see cref="ScavengePresenter.KnowledgeOf"/>).
/// </summary>
public static class BestiaryKnowledge
{
    public static IReadOnlyDictionary<string, BestiaryEntry> Colony { get; set; } = new Dictionary<string, BestiaryEntry>();
}
