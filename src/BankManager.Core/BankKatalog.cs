namespace BankManager.Core;

public sealed record BankVorlage(string Name, string Kuerzel, string Farbe, string Website, string Hinweis = "");

/// <summary>Known banks with brand colour and online banking address. Nothing here is required – the user can change all of it.</summary>
public static class BankKatalog
{
    public static readonly string[] Standard = ["ING-DiBa", "Crédit Agricole", "CosmosDirekt", "Bank11", "Sparkasse", "Klarna"];

    public static readonly IReadOnlyList<BankVorlage> Alle =
    [
        new("ING-DiBa", "ING", "#FF6200", "https://www.ing.de", "Girokonto, Extra-Konto, Depot"),
        new("Crédit Agricole", "CA", "#00A19A", "https://www.credit-agricole.fr"),
        new("CosmosDirekt", "CD", "#00539F", "https://www.cosmosdirekt.de", "Versicherungen, Tagesgeld, Altersvorsorge"),
        new("Bank11", "B11", "#E30613", "https://www.bank11.de", "Tagesgeld, Festgeld, Autofinanzierung"),
        new("Sparkasse", "S", "#FF0000", "https://www.sparkasse.de"),
        new("Klarna", "K", "#FFA8CD", "https://www.klarna.com/de/", "Klarna-Karte, Ratenkauf, Sparkonto"),
        new("DKB", "DKB", "#148DEA", "https://www.dkb.de"),
        new("Volksbank / Raiffeisenbank", "VR", "#0066B3", "https://www.vr.de"),
        new("Commerzbank", "CB", "#FFCC00", "https://www.commerzbank.de"),
        new("Deutsche Bank", "DB", "#0018A8", "https://www.deutsche-bank.de"),
        new("Postbank", "PB", "#FFCC00", "https://www.postbank.de"),
        new("comdirect", "cd", "#FFF500", "https://www.comdirect.de"),
        new("Consorsbank", "CS", "#00A0E1", "https://www.consorsbank.de"),
        new("N26", "N26", "#36A18B", "https://n26.com"),
        new("Revolut", "R", "#0075EB", "https://www.revolut.com"),
        new("Trade Republic", "TR", "#111111", "https://traderepublic.com"),
        new("PayPal", "PP", "#003087", "https://www.paypal.com"),
        new("Targobank", "TB", "#003A70", "https://www.targobank.de"),
        new("Santander", "SAN", "#EC0000", "https://www.santander.de"),
        new("HypoVereinsbank", "HVB", "#E2001A", "https://www.hypovereinsbank.de"),
        new("Sparda-Bank", "SB", "#005CA9", "https://www.sparda.de"),
        new("norisbank", "nb", "#00A3E0", "https://www.norisbank.de"),
        new("C24 Bank", "C24", "#063773", "https://c24.de"),
        new("Barclays", "BC", "#00AEEF", "https://www.barclays.de"),
    ];

    public static BankVorlage? Finden(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Vereinfachen(name);
        return Alle.FirstOrDefault(v => Vereinfachen(v.Name) == n)
               ?? Alle.FirstOrDefault(v => n.Length >= 3 && (Vereinfachen(v.Name).StartsWith(n) || n.StartsWith(Vereinfachen(v.Kuerzel)) && Vereinfachen(v.Kuerzel).Length >= 3));
    }

    public static Bank Anlegen(BankVorlage v) => new()
    {
        Name = v.Name,
        Kuerzel = v.Kuerzel,
        Farbe = v.Farbe,
        Website = v.Website,
    };

    public static List<Bank> StandardBanken() => Standard.Select(n => Anlegen(Finden(n)!)).ToList();

    public static string KuerzelAus(string name)
    {
        var worte = name.Split([' ', '-', '/', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (worte.Length == 0) return "?";
        if (worte.Length == 1) return worte[0].Length <= 3 ? worte[0] : worte[0][..1].ToUpperInvariant();
        return string.Concat(worte.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    private static string Vereinfachen(string s) =>
        new string(s.ToLowerInvariant()
            .Replace("é", "e").Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue")
            .Where(char.IsLetterOrDigit).ToArray());
}
