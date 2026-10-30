using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

/// <summary>
/// Gear mods over the wire: a party's modded gear joins as itself, the server refuses a build gear_mods.json does not
/// allow, Jury-rig works as an intent, and nothing leaks.
/// </summary>
public class GearModsNetTests
{
    [Fact]
    public void Modded_gear_joins_as_itself_and_a_forged_build_is_refused()
    {
        var f = new NetFixture();
        var cheat = f.Connect("Cheat");
        var forged = NetFixture.Party("C");
        // Two mods on a Mk I pistol: one slot.
        forged[0].Equipment![EquipSlot.Weapon] = "pistol+mod_iron_sight+mod_suppressor";
        cheat.Client.Join("Cheat", forged);
        Assert.Contains("pistol+mod_iron_sight", cheat.Last<JoinRefused>().Reason);

        var party = NetFixture.Party("A");
        party[0].Equipment![EquipSlot.Weapon] = "pistol@fine+mk3+mod_reflex_sight+mod_suppressor";
        party[0].Equipment![EquipSlot.Head] = "helmet_scrap";
        party[0].Pack!.Add("mod_flashlight");
        party[0].Survivor.Perks.Add("jury_rig");
        var a = f.Join("Alice", party);
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        var alice = f.Sim.State.Units.First(u => u.PlayerId == a.Id && u.Name == "A1");
        Assert.Equal("pistol@fine+mk3+mod_reflex_sight+mod_suppressor", alice.Equipment[EquipSlot.Weapon]);
        Assert.Equal("helmet_scrap", alice.Equipment[EquipSlot.Head]);

        f.EndTurnsUntil(() => f.Sim.State.CurrentUnitId == alice.Id, a, b);
        var fit = a.Do(new FieldMod(a.Id, alice.Id, "mod_flashlight"));
        Assert.True(fit.Ok, fit.Message);
        Assert.Equal("pistol@fine+mk3+mod_reflex_sight+mod_suppressor+mod_flashlight", alice.Equipment[EquipSlot.Weapon]);
        Assert.Contains(a.View.Units, u => u.Id == alice.Id && u.Equipment[EquipSlot.Weapon] == alice.Equipment[EquipSlot.Weapon]);
        f.AssertNoLeaks();
    }
}
