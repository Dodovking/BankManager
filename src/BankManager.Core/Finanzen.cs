namespace BankManager.Core;

public sealed record Hinweis(string Text, bool Warnung);

public static class Finanzen
{
    public static decimal Vermoegen(Tresor t) => t.AlleKonten.Where(k => k.Kontostand > 0).Sum(k => k.Kontostand);
    public static decimal Schulden(Tresor t) => t.AlleKonten.Where(k => k.Kontostand < 0).Sum(k => -k.Kontostand);
    public static decimal Netto(Tresor t) => t.AlleKonten.Sum(k => k.Kontostand);

    public static decimal EinnahmenProMonat(Tresor t) => t.Zahlungen.Where(z => z.Aktiv && z.IstEinnahme).Sum(z => z.ProMonat);
    public static decimal AusgabenProMonat(Tresor t) => t.Zahlungen.Where(z => z.Aktiv && !z.IstEinnahme).Sum(z => z.ProMonat);

    public static IReadOnlyList<(Bank Bank, decimal Summe)> ProBank(Tresor t) =>
        t.Banken.Select(b => (b, b.Summe)).OrderByDescending(x => x.Summe).ToList();

    /// <summary>Total balance over time: for every day on which any account changed, the sum of each account's latest known balance.</summary>
    public static IReadOnlyList<(DateTime Datum, decimal Summe)> Verlauf(Tresor t, DateTime? ab = null)
    {
        var konten = t.AlleKonten.Where(k => k.Verlauf.Count > 0).ToList();
        var tage = konten.SelectMany(k => k.Verlauf.Select(s => s.Datum.Date)).Distinct().Order().ToList();
        var result = new List<(DateTime, decimal)>();
        foreach (var tag in tage)
        {
            decimal summe = 0;
            foreach (var k in konten)
            {
                var letzter = k.Verlauf.LastOrDefault(s => s.Datum.Date <= tag);
                if (letzter != null) summe += letzter.Betrag;
            }
            result.Add((tag, summe));
        }
        if (ab is DateTime start)
        {
            var vorher = result.LastOrDefault(r => r.Item1 < start.Date);
            result = result.Where(r => r.Item1 >= start.Date).ToList();
            if (vorher != default) result.Insert(0, (start.Date, vorher.Item2));
        }
        return result;
    }

    public static IReadOnlyList<Hinweis> Hinweise(Tresor t, DateTime heute)
    {
        var list = new List<Hinweis>();
        foreach (var (bank, konto, karte) in t.AlleKarten)
        {
            var name = $"{(karte.Bezeichnung.Length > 0 ? karte.Bezeichnung : Texte.Name(karte.Typ))} ({bank.Name})";
            if (karte.IstAbgelaufen(heute)) list.Add(new($"Karte {name} ist seit {karte.GueltigText} abgelaufen.", true));
            else if (karte.LaeuftBaldAb(heute)) list.Add(new($"Karte {name} läuft {karte.GueltigText} ab – neue Karte sollte bald kommen.", true));
        }
        foreach (var bank in t.Banken)
        foreach (var k in bank.Konten)
        {
            var name = $"{k.Name} ({bank.Name})";
            if (k.Dispo is decimal dispo && dispo > 0 && k.Kontostand < 0 && -k.Kontostand >= dispo * 0.8m)
                list.Add(new($"{name}: Dispo fast ausgeschöpft.", true));
            if (k.LaufzeitBis is DateTime ende && ende.Date >= heute.Date && (ende.Date - heute.Date).TotalDays <= 30)
                list.Add(new($"{name}: Laufzeit endet am {ende:dd.MM.yyyy}.", true));
            if (k.Verlauf.Count > 0 && k.StandVom is DateTime vom && (heute - vom).TotalDays > 45)
                list.Add(new($"{name}: Kontostand seit {(int)(heute - vom).TotalDays} Tagen nicht aktualisiert.", false));
            if (k.Iban.Length > 0 && !Iban.IstGueltig(k.Iban))
                list.Add(new($"{name}: IBAN ist ungültig.", true));
        }
        if (t.Banken.Count > 0 && !t.AlleKonten.Any())
            list.Add(new("Lege bei deinen Banken Konten an, damit hier dein Gesamtvermögen erscheint.", false));
        return list;
    }

    public static string Euro(decimal betrag, string waehrung = "EUR")
    {
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        var text = betrag.ToString("#,##0.00", de);
        return waehrung switch
        {
            "EUR" => text + " €",
            "USD" => text + " $",
            "GBP" => text + " £",
            "CHF" => text + " CHF",
            _ => text + " " + waehrung,
        };
    }

    /// <summary>Parses "1.234,56", "1234.56", "-12,5 €" etc.</summary>
    public static bool BetragLesen(string? text, out decimal betrag)
    {
        betrag = 0;
        var s = new string((text ?? "").Where(c => char.IsDigit(c) || c is ',' or '.' or '-' or '+' or '−').ToArray()).Replace('−', '-');
        if (s.Length == 0) return false;
        int komma = s.LastIndexOf(','), punkt = s.LastIndexOf('.');
        if (komma >= 0 && punkt >= 0)
        {
            s = komma > punkt ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        }
        else if (komma >= 0)
        {
            s = s.Replace(',', '.');
        }
        else if (punkt >= 0 && s.Count(c => c == '.') > 1 || punkt >= 0 && s.Length - punkt - 1 == 3 && s.Count(c => c == '.') == 1 && !s.StartsWith("0."))
        {
            s = s.Replace(".", "");
        }
        return decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out betrag);
    }

    public static bool DatumLesen(string? text, out DateTime datum)
    {
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        return DateTime.TryParseExact((text ?? "").Trim(), ["d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "dd.MM.yy", "yyyy-MM-dd"], de,
            System.Globalization.DateTimeStyles.None, out datum);
    }
}
