using System.Text.Json.Serialization;

namespace BankManager.Core;

public enum ThemeChoice
{
    System,
    Hell,
    Dunkel,
}

public enum KontoArt
{
    Girokonto,
    Tagesgeld,
    Festgeld,
    Sparkonto,
    Depot,
    Kreditkarte,
    Kredit,
    Bausparvertrag,
    Versicherung,
    Sonstiges,
}

public enum KartenTyp
{
    Girocard,
    Debitkarte,
    Kreditkarte,
    Prepaidkarte,
    Sonstige,
}

public enum Rhythmus
{
    Woechentlich,
    Monatlich,
    Vierteljaehrlich,
    Halbjaehrlich,
    Jaehrlich,
}

public sealed class Tresor
{
    public int Format { get; set; } = 1;
    public List<Bank> Banken { get; set; } = [];
    public List<Zahlung> Zahlungen { get; set; } = [];

    public IEnumerable<Konto> AlleKonten => Banken.SelectMany(b => b.Konten);
    public IEnumerable<(Bank Bank, Konto Konto, Karte Karte)> AlleKarten =>
        Banken.SelectMany(b => b.Konten.SelectMany(k => k.Karten.Select(c => (b, k, c))));

    public Bank? BankVon(Konto konto) => Banken.FirstOrDefault(b => b.Konten.Contains(konto));
    public Konto? KontoMitId(string? id) => id == null ? null : AlleKonten.FirstOrDefault(k => k.Id == id);
}

public sealed class Bank
{
    public string Id { get; set; } = NeueId.Erzeugen();
    public string Name { get; set; } = "";
    public string Kuerzel { get; set; } = "";
    public string Farbe { get; set; } = "#3B82F6";
    public string Website { get; set; } = "";
    public string Bic { get; set; } = "";
    public string Hotline { get; set; } = "";
    public string Anmeldename { get; set; } = "";
    public string Passwort { get; set; } = "";
    public string Ansprechpartner { get; set; } = "";
    public string Notizen { get; set; } = "";
    public List<Konto> Konten { get; set; } = [];
    public FinTsZugang? FinTs { get; set; }

    [JsonIgnore] public bool AutoAbruf => FinTs is { Aktiv: true };
    [JsonIgnore] public decimal Summe => Konten.Sum(k => k.Kontostand);
    [JsonIgnore] public string AnzeigeKuerzel => Kuerzel.Length > 0 ? Kuerzel : BankKatalog.KuerzelAus(Name);
}

public sealed class Konto
{
    public string Id { get; set; } = NeueId.Erzeugen();
    public string Name { get; set; } = "";
    public KontoArt Art { get; set; } = KontoArt.Girokonto;
    public string Iban { get; set; } = "";
    public string Inhaber { get; set; } = "";
    public string Waehrung { get; set; } = "EUR";
    public decimal Kontostand { get; set; }
    public DateTime? StandVom { get; set; }
    public decimal? Zinssatz { get; set; }
    public decimal? Dispo { get; set; }
    public DateTime? LaufzeitBis { get; set; }
    public string Notizen { get; set; } = "";
    public List<Stand> Verlauf { get; set; } = [];
    public List<Karte> Karten { get; set; } = [];

    [JsonIgnore] public bool IstSchuld => Kontostand < 0;

    /// <summary>Sets the balance and records it in the history (one entry per day, the last one wins).</summary>
    public void StandSetzen(decimal betrag, DateTime? wann = null)
    {
        var t = wann ?? DateTime.Now;
        Kontostand = betrag;
        StandVom = t;
        Verlauf.RemoveAll(s => s.Datum.Date == t.Date);
        Verlauf.Add(new Stand { Datum = t, Betrag = betrag });
        Verlauf.Sort((a, b) => a.Datum.CompareTo(b.Datum));
    }
}

/// <summary>Read-only FinTS access (balances only). The PIN is only kept if the user chose "PIN merken".</summary>
public sealed class FinTsZugang
{
    public bool Aktiv { get; set; }
    public string Url { get; set; } = "";
    public string Blz { get; set; } = "";
    public string Benutzerkennung { get; set; } = "";
    public string Pin { get; set; } = "";
    public bool PinMerken { get; set; }
    public string? KundensystemId { get; set; }
    public DateTime? LetzterAbruf { get; set; }
    public string LetzterFehler { get; set; } = "";
}

public sealed class Stand
{
    public DateTime Datum { get; set; }
    public decimal Betrag { get; set; }
}

public sealed class Karte
{
    public string Id { get; set; } = NeueId.Erzeugen();
    public string Bezeichnung { get; set; } = "";
    public KartenTyp Typ { get; set; } = KartenTyp.Debitkarte;
    public string Netzwerk { get; set; } = "";
    public string LetzteZiffern { get; set; } = "";
    public int? GueltigMonat { get; set; }
    public int? GueltigJahr { get; set; }
    public decimal? Limit { get; set; }
    public string Notizen { get; set; } = "";

    [JsonIgnore]
    public DateTime? LaeuftAb => GueltigMonat is int m and >= 1 and <= 12 && GueltigJahr is int j and > 2000
        ? new DateTime(j, m, 1).AddMonths(1).AddDays(-1)
        : null;

    [JsonIgnore]
    public string GueltigText => GueltigMonat is int m && GueltigJahr is int j ? $"{m:00}/{j % 100:00}" : "–";

    public bool LaeuftBaldAb(DateTime heute, int tage = 60) => LaeuftAb is DateTime d && d >= heute.Date && (d - heute.Date).TotalDays <= tage;
    public bool IstAbgelaufen(DateTime heute) => LaeuftAb is DateTime d && d < heute.Date;
}

public sealed class Zahlung
{
    public string Id { get; set; } = NeueId.Erzeugen();
    public string Name { get; set; } = "";
    public decimal Betrag { get; set; }
    public bool IstEinnahme { get; set; }
    public Rhythmus Rhythmus { get; set; } = Rhythmus.Monatlich;
    public int? Tag { get; set; }
    public string? KontoId { get; set; }
    public string Kategorie { get; set; } = "";
    public bool Aktiv { get; set; } = true;
    public string Notizen { get; set; } = "";

    [JsonIgnore]
    public decimal ProMonat => Math.Round(Math.Abs(Betrag) * Rhythmus switch
    {
        Rhythmus.Woechentlich => 52m / 12m,
        Rhythmus.Vierteljaehrlich => 1m / 3m,
        Rhythmus.Halbjaehrlich => 1m / 6m,
        Rhythmus.Jaehrlich => 1m / 12m,
        _ => 1m,
    }, 2);
}

public sealed class Einstellungen
{
    public ThemeChoice Design { get; set; } = ThemeChoice.Dunkel;
    public int AutoSperreMinuten { get; set; } = 10;
    public bool BetraegeVerbergen { get; set; }
    public bool UpdatesPruefen { get; set; } = true;
    public string FinTsProduktnummer { get; set; } = "";
    public int FensterBreite { get; set; }
    public int FensterHoehe { get; set; }
    public bool Maximiert { get; set; }
}

public static class NeueId
{
    public static string Erzeugen() => Guid.NewGuid().ToString("N")[..12];
}

public static class Texte
{
    public static string Name(KontoArt a) => a switch
    {
        KontoArt.Girokonto => "Girokonto",
        KontoArt.Tagesgeld => "Tagesgeld",
        KontoArt.Festgeld => "Festgeld",
        KontoArt.Sparkonto => "Sparkonto",
        KontoArt.Depot => "Depot",
        KontoArt.Kreditkarte => "Kreditkartenkonto",
        KontoArt.Kredit => "Kredit / Ratenkauf",
        KontoArt.Bausparvertrag => "Bausparvertrag",
        KontoArt.Versicherung => "Versicherung / Vorsorge",
        _ => "Sonstiges",
    };

    public static string Name(KartenTyp t) => t switch
    {
        KartenTyp.Girocard => "Girocard",
        KartenTyp.Debitkarte => "Debitkarte",
        KartenTyp.Kreditkarte => "Kreditkarte",
        KartenTyp.Prepaidkarte => "Prepaidkarte",
        _ => "Sonstige Karte",
    };

    public static string Name(Rhythmus r) => r switch
    {
        Rhythmus.Woechentlich => "wöchentlich",
        Rhythmus.Monatlich => "monatlich",
        Rhythmus.Vierteljaehrlich => "vierteljährlich",
        Rhythmus.Halbjaehrlich => "halbjährlich",
        _ => "jährlich",
    };
}
