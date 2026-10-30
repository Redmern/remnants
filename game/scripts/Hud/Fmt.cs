using System;
using System.Globalization;

namespace Remnants.Game.Hud;

/// <summary>One way to write numbers everywhere: whole amounts, "12.4k" past ten thousand, signed deltas.</summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A stock amount: "7", "1 240", "12.4k".</summary>
    public static string Amount(double value)
    {
        var abs = Math.Abs(value);
        if (abs >= 10_000) return (value / 1000).ToString("0.#", Inv) + "k";
        return Math.Floor(value + 1e-9).ToString("#,0", Inv).Replace(',', ' ');
    }

    /// <summary>A change: "+3", "−1.5", "±0". Uses a real minus sign so columns line up.</summary>
    public static string Delta(double value, string format = "0.#")
    {
        var rounded = Math.Round(value, 1);
        if (rounded == 0) return "±0";
        var text = Math.Abs(rounded).ToString(format, Inv);
        return (rounded > 0 ? "+" : "−") + text;
    }

    /// <summary>A multiplier: "×1.25".</summary>
    public static string Factor(double value) => "×" + value.ToString("0.##", Inv);

    public static string Percent(double fraction) => Math.Round(fraction * 100).ToString("0", Inv) + "%";

    public static string Decimal(double value, string format = "0.##") => value.ToString(format, Inv);
}
