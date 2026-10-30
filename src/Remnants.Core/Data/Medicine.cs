using Remnants.Core.Survivors;

namespace Remnants.Core.Data;

/// <summary>What sort of condition a <see cref="ConditionDef"/> is; the Med Bay treats them in a priority order of these.</summary>
public enum ConditionKind { Infection, Fracture, Sickness, Dependency, Effect }

/// <summary>What the Med Bay treats first: a bleeding (worsening or undressed) wound, an infection, a fracture, a sickness, pain.</summary>
public enum TreatPriority { Bleeding, Infection, Fracture, Sickness, Pain }

/// <summary>
/// The medicine system (data/medicine.json): a handful of conditions with severity, duration and treatment; how wounds get
/// infected; fractures and splints; pain from severe injuries and painkillers (with a mild dependency); sicknesses (food,
/// Husk meat); the Med Bay's auto-treatment; surgery kits; blood donors; and the field side (stims, the Calm injector).
/// Treatment items are plain items with a <c>use</c> block (items.json). See ARCHITECTURE.md and BALANCE.md "Medicine".
/// </summary>
public sealed record MedicineDef
{
    public List<ConditionDef> Conditions { get; init; } = new();
    public InfectionRules Infection { get; init; } = new();
    public FractureRules Fracture { get; init; } = new();
    public PainRules Pain { get; init; } = new();
    public PainkillerRules Painkiller { get; init; } = new();

    /// <summary>Sickness kinds (what <c>Sickness.Apply</c> is called with: food, spoiled, husk) → condition id.</summary>
    public Dictionary<string, string> SicknessKinds { get; init; } = new(StringComparer.Ordinal);

    public MedBayCareRules MedBay { get; init; } = new();
    public SurgeryKitRules Surgery { get; init; } = new();
    public DonorRules Donor { get; init; } = new();
    public FieldMedicineRules Field { get; init; } = new();
    public MedicineMigrationRules Migration { get; init; } = new();

    /// <summary>Ingredient adapters: ids other features own, with a stand-in when that feature is not in the data.</summary>
    public List<IngredientAdapter> Ingredients { get; init; } = [.. DefaultIngredients];

    /// <summary>Used without medicine.json too: recipes.json names herbs either way.</summary>
    public static readonly IReadOnlyList<IngredientAdapter> DefaultIngredients = [new IngredientAdapter { Id = "herbs", Fallback = "food", Ratio = 1.5 }];
}

/// <summary>
/// One condition. Untreated it grows by <see cref="GrowPerTick"/> (an infection) or fades by <see cref="RecoverPerTick"/>
/// (a sickness), under treatment (a patient with medicine, or <see cref="BodyCondition.Treated"/>) it fades by
/// <see cref="TreatedRecoverPerTick"/>. Job output drops by up to <see cref="OutputPenalty"/> (at severity 1) and health
/// drains from <see cref="HealthDrainFrom"/>. In the field it costs <see cref="FieldApPenalty"/> AP from
/// <see cref="FieldApFrom"/>. Items whose <c>use.cures</c> name it cure it.
/// </summary>
public sealed record ConditionDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public ConditionKind Kind { get; init; }
    public string Description { get; init; } = "";

    /// <summary>What the Body tab suggests doing about it.</summary>
    public string Treatment { get; init; } = "";

    /// <summary>Shown as a fever (a flag for the UI; the penalty is <see cref="OutputPenalty"/>).</summary>
    public bool Fever { get; init; }
    public double OutputPenalty { get; init; }
    public double GrowPerTick { get; init; }
    public double RecoverPerTick { get; init; }
    public double TreatedRecoverPerTick { get; init; }
    public double HealthDrainFrom { get; init; } = 2;
    public double HealthDrainPerTick { get; init; }

    /// <summary>Mood change per tick while it lasts (a dependency's dip), times severity.</summary>
    public double MoodPerTick { get; init; }
    public int FieldApPenalty { get; init; }
    public double FieldApFrom { get; init; } = 0.5;

    /// <summary>An infection stops the injury on its part from healing (naturally or at the Med Bay) until it is cured.</summary>
    public bool BlocksHealing { get; init; }
}

/// <summary>
/// A dirty wound gets infected. Each tick an injured part of at least <see cref="MinSeverity"/> rolls
/// <see cref="ChancePerTick"/> × (bandaged: <see cref="BandagedMultiplier"/>, clean: <see cref="CleanMultiplier"/>) × (a
/// patient: <see cref="PatientMultiplier"/>). An infection starts at <see cref="StartSeverity"/> scaled by the wound.
/// </summary>
public sealed record InfectionRules
{
    public string Condition { get; init; } = "infection";
    public double MinSeverity { get; init; } = 0.25;
    public double ChancePerTick { get; init; } = 0.004;
    public double BandagedMultiplier { get; init; } = 0.35;
    public double CleanMultiplier { get; init; }
    public double PatientMultiplier { get; init; } = 0.5;
    public double StartSeverity { get; init; } = 0.15;

    /// <summary>Condition a failed surgery leaves on the fitted part (instead of a plain wound infection).</summary>
    public string SurgicalCondition { get; init; } = "surgical_infection";
}

/// <summary>
/// A hard hit on an arm or leg can break it: chance <see cref="Chance"/> when the hit leaves the part at
/// <see cref="MinSeverity"/> or worse. A fracture costs <see cref="Penalty"/> efficiency on the part until splinted
/// (<see cref="SplintedPenalty"/>); an unsplinted fracture heals at <see cref="UnsplintedHealMultiplier"/> of the normal
/// rate, a splinted one at <see cref="SplintedHealMultiplier"/>.
/// </summary>
public sealed record FractureRules
{
    public string Condition { get; init; } = "fracture";
    public double Chance { get; init; } = 0.35;
    public double MinSeverity { get; init; } = 0.45;
    public double Penalty { get; init; } = 0.25;
    public double SplintedPenalty { get; init; } = 0.08;
    public double UnsplintedHealMultiplier { get; init; } = 0.5;
    public double SplintedHealMultiplier { get; init; } = 1.25;
}

/// <summary>
/// Severe injuries hurt: from a worst injury of <see cref="Threshold"/> a survivor is in pain (and worn out), job output
/// drops by <see cref="OutputPenalty"/> and in the field they lose <see cref="FieldApPenalty"/> AP (0 as shipped: a
/// field AP cost made every hard fight a death spiral, see BALANCE.md "Medicine"). A painkiller takes it away while it lasts.
/// </summary>
public sealed record PainRules
{
    public double Threshold { get; init; } = 0.55;
    public double OutputPenalty { get; init; } = 0.15;
    public int FieldApPenalty { get; init; }
}

/// <summary>
/// Each dose adds 1 to the body's dose count, which decays by <see cref="DoseDecayPerTick"/>. At
/// <see cref="DependencyDoses"/> or more a dependency starts (or deepens by <see cref="DependencySeverity"/>).
/// </summary>
public sealed record PainkillerRules
{
    public double DoseDecayPerTick { get; init; } = 0.02;
    public double DependencyDoses { get; init; } = 3;
    public string DependencyCondition { get; init; } = "dependency";
    public double DependencySeverity { get; init; } = 0.4;
}

/// <summary>
/// Auto-treatment at the Med Bay: each tick it uses at most <see cref="TreatmentsPerTick"/> stocked items on its patients, in
/// the colony's priority order (default <see cref="DefaultPriorities"/>). Supplies are worst quality first.
/// </summary>
public sealed record MedBayCareRules
{
    public List<TreatPriority> DefaultPriorities { get; init; } = [TreatPriority.Bleeding, TreatPriority.Infection, TreatPriority.Fracture, TreatPriority.Sickness, TreatPriority.Pain];
    public int TreatmentsPerTick { get; init; } = 1;

    /// <summary>A patient on medicine (the stockpile resource) has infections fade at this share of their treated rate even without antibiotics.</summary>
    public double MedicineEasesInfection { get; init; } = 0.5;
}

/// <summary>A kit used up by a surgery that has one in stock: better odds (times the kit's quality) and fewer infections.</summary>
public sealed record SurgeryKitDef
{
    public string Item { get; init; } = "";
    public double SuccessBonus { get; init; }

    /// <summary>Lowest prosthetic tier it helps with (a suture kit is for simpler work, a surgical kit for anything).</summary>
    public int FromTier { get; init; }
    public int ToTier { get; init; } = 99;
}

public sealed record SurgeryKitRules
{
    /// <summary>Best first: a surgery takes the first kit it can use.</summary>
    public List<SurgeryKitDef> Kits { get; init; } = new();

    /// <summary>Severity of the infection a failed surgery leaves on the part.</summary>
    public double InfectionSeverity { get; init; } = 0.3;
}

/// <summary>A blood bag needs a donor at home: an adult, not a patient, with at least <see cref="MinHealth"/>; the healthiest gives.</summary>
public sealed record DonorRules
{
    public double MinHealth { get; init; } = 70;
}

/// <summary>Field-only numbers (a stim's AP and crash are in its item).</summary>
public sealed record FieldMedicineRules
{
    /// <summary>Most AP conditions and pain may take off a turn (and never below 2 AP, see the profile).</summary>
    public int MaxApPenalty { get; init; } = 1;

    /// <summary>Medicine skill a survivor practises with each treatment they give in the field.</summary>
    public double SkillPerTreatment { get; init; } = 0.3;
}

/// <summary>Saves from before medicine: part of the old medicine stock becomes antiseptic (2 medicine → 1 antiseptic).</summary>
public sealed record MedicineMigrationRules
{
    public string AntisepticItem { get; init; } = "antiseptic";
    public double ShareConverted { get; init; } = 0.5;
    public double MedicinePerAntiseptic { get; init; } = 2;
}

/// <summary>
/// An ingredient another feature owns (herbs from food-cooking, alcohol from the vehicles' still): recipes name
/// <see cref="Id"/>; when no resource of that id is loaded, the recipe uses <see cref="Fallback"/> × <see cref="Ratio"/> instead.
/// </summary>
public sealed record IngredientAdapter
{
    public string Id { get; init; } = "";
    public string Fallback { get; init; } = "";
    public double Ratio { get; init; } = 1;
}

/// <summary>The medicine data indexed for lookups. Immutable.</summary>
public sealed class MedicineData
{
    public MedicineDef Def { get; }
    public IReadOnlyDictionary<string, ConditionDef> Conditions { get; }

    public MedicineData(MedicineDef def)
    {
        Def = def;
        var conditions = new Dictionary<string, ConditionDef>(StringComparer.Ordinal);
        foreach (var c in def.Conditions)
        {
            if (string.IsNullOrWhiteSpace(c.Id) || !conditions.TryAdd(c.Id, c)) throw new GameDataException($"medicine.json: condition '{c.Id}' is missing an id or used twice.");
            if (c.OutputPenalty is < 0 or > 1) throw new GameDataException($"medicine.json: condition '{c.Id}' needs an outputPenalty in 0..1.");
        }
        Conditions = conditions;
        foreach (var id in new[] { def.Infection.Condition, def.Infection.SurgicalCondition, def.Fracture.Condition, def.Painkiller.DependencyCondition, Body.PainkillerConditionId })
            if (!conditions.ContainsKey(id)) throw new GameDataException($"medicine.json: the rules name condition '{id}', which is not defined.");
        foreach (var (kind, id) in def.SicknessKinds)
            if (!conditions.ContainsKey(id)) throw new GameDataException($"medicine.json: sickness kind '{kind}' names unknown condition '{id}'.");
        if (def.MedBay.DefaultPriorities.Distinct().Count() != def.MedBay.DefaultPriorities.Count)
            throw new GameDataException("medicine.json: medBay.defaultPriorities lists a priority twice.");
    }

    public ConditionDef? Condition(string id) => Conditions.GetValueOrDefault(id);

    public InfectionRules Infection => Def.Infection;
    public FractureRules Fracture => Def.Fracture;
    public PainRules Pain => Def.Pain;
    public PainkillerRules Painkiller => Def.Painkiller;
}
