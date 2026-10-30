using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Puts a prosthetic on a survivor the way Core wants it: crude and mechanical parts on a missing limb strap on at once
/// (<see cref="FitProsthetic"/>); everything else is booked as surgery (<see cref="ScheduleSurgery"/>). Replacing a
/// healthy part is an augmentation: a confirm dialog names the humanity hit first, then books it as elective.
/// </summary>
public static class CyberBooking
{
    /// <summary>"Fit" or "Book surgery" (or "Augment…") for the button that will call <see cref="Book"/>.</summary>
    public static string Verb(ColonySim sim, int survivorId, BodyPart part, string itemId)
    {
        if (sim.Data.Cybernetics is not { } cyber || sim.State.FindSurvivor(survivorId) is not { } s) return "Fit";
        var node = cyber.Node(sim.Scavenge?.Items.GetValueOrDefault(itemId)?.DefinedId ?? itemId);
        if (s.Body[part].Condition == PartCondition.Healthy) return "Augment…";
        return node is not null && node.Tier < cyber.Surgery.FromTier && s.Body[part].Condition == PartCondition.Missing ? "Fit" : "Book surgery";
    }

    public static void Book(ColonySession session, Node parent, int survivorId, BodyPart part, string itemId)
    {
        var sim = session.Sim;
        if (sim.State.FindSurvivor(survivorId) is not { } s) return;
        var cyber = sim.Data.Cybernetics;
        var item = sim.Scavenge?.Items.GetValueOrDefault(itemId);
        var node = cyber?.Node(item?.DefinedId ?? itemId);
        var condition = s.Body[part].Condition;
        if (cyber is null || node is null || condition == PartCondition.Missing && node.Tier < cyber.Surgery.FromTier)
        {
            session.Execute(new FitProsthetic(survivorId, part, itemId));
            return;
        }
        if (condition != PartCondition.Healthy)
        {
            session.Execute(new ScheduleSurgery(survivorId, part, itemId));
            return;
        }
        // Let Core say no first (tier too low, a child): only a real augmentation gets the question.
        var check = session.Sim.Execute(new ScheduleSurgery(survivorId, part, itemId));
        if (check.Error != CommandError.ElectiveNotAllowed || cyber.Tier(node.Tier).ElectiveHumanity is not { } cost
            || node.Tier < cyber.Surgery.ElectiveFromTier || s.IsChild)
        {
            session.Execute(new ScheduleSurgery(survivorId, part, itemId));
            return;
        }
        var after = System.Math.Max(0, s.Body.Humanity - cost);
        var band = CyberText.Humanity(cyber.Humanity, after);
        // Over the whole HUD, not inside the (small) panel that asked.
        var host = parent;
        while (host is not CanvasLayer && host.GetParent() is { } up) host = up;
        ConfirmDialog.Ask(host, "Augment a healthy limb?",
            $"{s.Name}'s {CyberText.Part(part).ToLowerInvariant()} is healthy. Replacing it with a {item!.Name} is an elective augmentation: " +
            $"−{cost:0} humanity ({s.Body.Humanity:0} → {after:0}, {band.Name}). Low humanity caps mood and strains partners and children. " +
            "It cannot be undone.",
            "Augment", () => session.Execute(new ScheduleSurgery(survivorId, part, itemId, Elective: true)), danger: true);
    }
}
