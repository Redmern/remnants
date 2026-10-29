namespace Remnants.Core.Colony;

/// <summary>How the player feels about a pair: encouraged pairs bond faster, discouraged ones not at all.</summary>
public enum PairStance { Neutral, Encourage, Discourage }

/// <summary>Affinity between two survivors (<see cref="A"/> &lt; <see cref="B"/>). Plain data.</summary>
public sealed class Relationship
{
    public int A { get; set; }
    public int B { get; set; }
    public double Affinity { get; set; }
    public PairStance Stance { get; set; }

    public bool Involves(int id) => A == id || B == id;

    public int Other(int id) => A == id ? B : A;
}
