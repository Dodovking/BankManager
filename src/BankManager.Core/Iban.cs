using System.Numerics;
using System.Text;

namespace BankManager.Core;

public static class Iban
{
    private static readonly Dictionary<string, int> Laengen = new()
    {
        ["AD"] = 24, ["AE"] = 23, ["AT"] = 20, ["BA"] = 20, ["BE"] = 16, ["BG"] = 22, ["CH"] = 21, ["CY"] = 28,
        ["CZ"] = 24, ["DE"] = 22, ["DK"] = 18, ["EE"] = 20, ["ES"] = 24, ["FI"] = 18, ["FR"] = 27, ["GB"] = 22,
        ["GI"] = 23, ["GR"] = 27, ["HR"] = 21, ["HU"] = 28, ["IE"] = 22, ["IS"] = 26, ["IT"] = 27, ["LI"] = 21,
        ["LT"] = 20, ["LU"] = 20, ["LV"] = 21, ["MC"] = 27, ["MT"] = 31, ["NL"] = 18, ["NO"] = 15, ["PL"] = 28,
        ["PT"] = 25, ["RO"] = 24, ["RS"] = 22, ["SE"] = 24, ["SI"] = 19, ["SK"] = 24, ["SM"] = 27, ["TR"] = 26,
        ["UA"] = 29, ["VA"] = 22,
    };

    public static string Normalisieren(string? iban) =>
        new string((iban ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public static string Formatieren(string? iban)
    {
        var n = Normalisieren(iban);
        var sb = new StringBuilder();
        for (int i = 0; i < n.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(n[i]);
        }
        return sb.ToString();
    }

    /// <summary>Null if the IBAN is valid (or empty), otherwise a German error message.</summary>
    public static string? Pruefen(string? iban)
    {
        var n = Normalisieren(iban);
        if (n.Length == 0) return null;
        if (n.Length < 5 || !char.IsLetter(n[0]) || !char.IsLetter(n[1]) || !char.IsDigit(n[2]) || !char.IsDigit(n[3]))
            return "Eine IBAN beginnt mit zwei Buchstaben (Land) und zwei Prüfziffern, z. B. DE12 …";
        var land = n[..2];
        if (Laengen.TryGetValue(land, out var soll) && n.Length != soll)
            return $"Eine IBAN aus {land} hat {soll} Zeichen – diese hat {n.Length}.";
        if (n.Length > 34) return "Eine IBAN hat höchstens 34 Zeichen.";
        return Mod97(n) == 1 ? null : "Die Prüfziffer stimmt nicht – bitte die IBAN noch einmal vergleichen.";
    }

    public static bool IstGueltig(string? iban) => Normalisieren(iban).Length > 0 && Pruefen(iban) == null;

    /// <summary>For German IBANs: the 8-digit Bankleitzahl.</summary>
    public static string? Bankleitzahl(string? iban)
    {
        var n = Normalisieren(iban);
        return n.Length == 22 && n.StartsWith("DE") ? n.Substring(4, 8) : null;
    }

    /// <summary>"DE89 •••• •••• 3000" style, for hidden mode.</summary>
    public static string Maskieren(string? iban)
    {
        var n = Normalisieren(iban);
        if (n.Length < 8) return Formatieren(n);
        return n[..4] + " •••• •••• " + n[^4..];
    }

    private static int Mod97(string n)
    {
        var umgestellt = n[4..] + n[..4];
        var ziffern = new StringBuilder();
        foreach (var c in umgestellt)
        {
            if (char.IsDigit(c)) ziffern.Append(c);
            else ziffern.Append(c - 'A' + 10);
        }
        return (int)(BigInteger.Parse(ziffern.ToString()) % 97);
    }
}

public static class IbanBau
{
    /// <summary>Builds an IBAN with correct check digits from country code and BBAN (used for demo data and tests).</summary>
    public static string Erzeugen(string land, string bban)
    {
        var ziffern = new System.Text.StringBuilder();
        foreach (var c in (bban + land + "00").ToUpperInvariant())
            ziffern.Append(char.IsDigit(c) ? c.ToString() : (c - 'A' + 10).ToString());
        int pruef = 98 - (int)(System.Numerics.BigInteger.Parse(ziffern.ToString()) % 97);
        return land + pruef.ToString("00") + bban;
    }
}
