using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;

namespace Remnants.Net;

/// <summary>
/// JSON for the wire: Core's save options (camelCase, string enums), compact, plus a <c>kind</c> discriminator for
/// Core's <see cref="IScavengeCommand"/> records so Core itself needs no serialization attributes.
/// </summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = Create();

    public static string Serialize(Message message) => JsonSerializer.Serialize(message, Options);

    public static Message Deserialize(string json) =>
        JsonSerializer.Deserialize<Message>(json, Options) ?? throw new JsonException("Empty message.");

    private static JsonSerializerOptions Create()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(IScavengeCommand)) return;
            info.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                UnknownDerivedTypeHandling = System.Text.Json.Serialization.JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(Move), "move"),
                    new JsonDerivedType(typeof(Attack), "attack"),
                    new JsonDerivedType(typeof(UseItem), "useItem"),
                    new JsonDerivedType(typeof(PickUp), "pickUp"),
                    new JsonDerivedType(typeof(TakeAll), "takeAll"),
                    new JsonDerivedType(typeof(DropItem), "dropItem"),
                    new JsonDerivedType(typeof(StashItem), "stashItem"),
                    new JsonDerivedType(typeof(Salvage), "salvage"),
                    new JsonDerivedType(typeof(Recruit), "recruit"),
                    new JsonDerivedType(typeof(EndTurn), "endTurn"),
                    new JsonDerivedType(typeof(Extract), "extract"),
                    new JsonDerivedType(typeof(SetStance), "setStance"),
                    new JsonDerivedType(typeof(Lookout), "lookout"),
                    new JsonDerivedType(typeof(ProposeAlliance), "proposeAlliance"),
                    new JsonDerivedType(typeof(RespondAlliance), "respondAlliance"),
                    new JsonDerivedType(typeof(BreakAlliance), "breakAlliance"),
                    new JsonDerivedType(typeof(PingTile), "ping"),
                    new JsonDerivedType(typeof(GiveItem), "giveItem"),
                    new JsonDerivedType(typeof(Carry), "carry"),
                    new JsonDerivedType(typeof(PutDown), "putDown"),
                    new JsonDerivedType(typeof(Hack), "hack"),
                    new JsonDerivedType(typeof(DeployDrone), "deployDrone"),
                    new JsonDerivedType(typeof(FlyDrone), "flyDrone"),
                    new JsonDerivedType(typeof(RecallDrone), "recallDrone"),
                    new JsonDerivedType(typeof(MarkTarget), "markTarget"),
                    new JsonDerivedType(typeof(DropPayload), "dropPayload"),
                    new JsonDerivedType(typeof(DroneHeal), "droneHeal"),
                    new JsonDerivedType(typeof(DroneLoad), "droneLoad"),
                    new JsonDerivedType(typeof(DroneFerry), "droneFerry"),
                    new JsonDerivedType(typeof(DroneFire), "droneFire"),
                    new JsonDerivedType(typeof(FieldMod), "fieldMod"),
                },
            };
        });
        return new JsonSerializerOptions(Json.Options) { WriteIndented = false, TypeInfoResolver = resolver };
    }
}
