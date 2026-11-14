using BankManager.Core;
using Xunit;

namespace BankManager.Tests;

public sealed class TempDir : IDisposable
{
    public string Pfad { get; } = Path.Combine(Path.GetTempPath(), "bm-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Pfad);
    public void Dispose() { try { Directory.Delete(Pfad, true); } catch { } }
}

public class TresorTests
{
    private static Tresor Beispiel()
    {
        var t = new Tresor { Banken = BankKatalog.StandardBanken() };
        var ing = t.Banken[0];
        ing.Passwort = "geheim123";
        var giro = new Konto { Name = "Girokonto", Iban = "DE89 3704 0044 0532 0130 00" };
        giro.StandSetzen(1234.56m, new DateTime(2026, 1, 5));
        giro.Karten.Add(new Karte { Bezeichnung = "Visa Debit", LetzteZiffern = "4711", GueltigMonat = 3, GueltigJahr = 2029 });
        ing.Konten.Add(giro);
        t.Zahlungen.Add(new Zahlung { Name = "Miete", Betrag = 800, KontoId = giro.Id });
        return t;
    }

    [Fact]
    public void SpeichernUndOeffnen_ErgibtGleicheDaten()
    {
        using var dir = new TempDir();
        var pfad = Path.Combine(dir.Pfad, "tresor.bmtresor");
        var datei = new TresorDatei(pfad, 2000);
        datei.Anlegen("Mein Passwort", Beispiel());

        var geladen = new TresorDatei(pfad).Oeffnen("Mein Passwort");
        Assert.Equal(6, geladen.Banken.Count);
        Assert.Equal("ING-DiBa", geladen.Banken[0].Name);
        Assert.Equal(1234.56m, geladen.AlleKonten.Single().Kontostand);
        Assert.Equal("geheim123", geladen.Banken[0].Passwort);
        Assert.Equal("4711", geladen.AlleKarten.Single().Karte.LetzteZiffern);
        Assert.Equal(geladen.AlleKonten.Single().Id, geladen.Zahlungen.Single().KontoId);
    }

    [Fact]
    public void Datei_EnthaeltKeinenKlartext()
    {
        using var dir = new TempDir();
        var pfad = Path.Combine(dir.Pfad, "tresor.bmtresor");
        new TresorDatei(pfad, 2000).Anlegen("pw", Beispiel());
        var inhalt = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(pfad));
        Assert.DoesNotContain("geheim123", inhalt);
        Assert.DoesNotContain("ING", inhalt);
        Assert.DoesNotContain("DE89", inhalt);
    }

    [Fact]
    public void FalschesPasswort_WirftUndEntsperrtNicht()
    {
        using var dir = new TempDir();
        var pfad = Path.Combine(dir.Pfad, "tresor.bmtresor");
        new TresorDatei(pfad, 2000).Anlegen("richtig", Beispiel());
        var d = new TresorDatei(pfad);
        Assert.Throws<FalschesPasswortException>(() => d.Oeffnen("falsch"));
        Assert.False(d.Entsperrt);
    }

    [Fact]
    public void ManipulierteDatei_WirdErkannt()
    {
        using var dir = new TempDir();
        var pfad = Path.Combine(dir.Pfad, "tresor.bmtresor");
        new TresorDatei(pfad, 2000).Anlegen("pw", Beispiel());
        var bytes = File.ReadAllBytes(pfad);
        bytes[^5] ^= 0xFF;
        File.WriteAllBytes(pfad, bytes);
        Assert.Throws<FalschesPasswortException>(() => new TresorDatei(pfad).Oeffnen("pw"));
    }

    [Fact]
    public void FremdeDatei_IstDefekt()
    {
        using var dir = new TempDir();
        var pfad = Path.Combine(dir.Pfad, "x.bmtresor");
        File.WriteAllText(pfad, "hallo welt, das ist kein tresor, sondern einfach nur text");
        Assert.Throws<TresorDefektException>(() => new TresorDatei(pfad).Oeffnen("pw"));
    }

    [Fact]
    public void PasswortAendern_AltesGehtNichtMehr()
    {
        using var dir = new TempDir();
        var pfad = Path.Combine(dir.Pfad, "tresor.bmtresor");
        var d = new TresorDatei(pfad, 2000);
        var t = Beispiel();
        d.Anlegen("alt", t);
        Assert.Throws<FalschesPasswortException>(() => d.PasswortAendern("falsch", "neu", t));
        d.PasswortAendern("alt", "neu", t);
        Assert.False(new TresorDatei(pfad).PasswortPruefen("alt"));
        Assert.Single(new TresorDatei(pfad).Oeffnen("neu").AlleKonten);
    }

    [Fact]
    public void Speichern_LegtTagesSicherungAn_UndSperrenVerhindertSpeichern()
    {
        using var dir = new TempDir();
        var pfad = Path.Combine(dir.Pfad, "tresor.bmtresor");
        var d = new TresorDatei(pfad, 2000);
        var t = Beispiel();
        d.Anlegen("pw", t);
        t.Banken.RemoveAt(5);
        d.Speichern(t);
        Assert.Single(Directory.GetFiles(d.SicherungsOrdner));
        Assert.Equal(6, TresorDatei.Importieren(Directory.GetFiles(d.SicherungsOrdner)[0], "pw").Banken.Count);
        Assert.Equal(5, new TresorDatei(pfad).Oeffnen("pw").Banken.Count);
        d.Sperren();
        Assert.Throws<InvalidOperationException>(() => d.Speichern(t));
    }
}

public class BeiseiteTests
{
    [Fact]
    public void Beiseitelegen_BehaeltAlteDatei_UndErlaubtNeuenTresor()
    {
        using var dir = new TempDir();
        var pfad = Path.Combine(dir.Pfad, "tresor.bmtresor");
        var d = new TresorDatei(pfad, 2000);
        d.Anlegen("vergessen", new Tresor());
        var alt = d.Beiseitelegen();
        Assert.False(d.Existiert);
        Assert.False(d.Entsperrt);
        Assert.True(File.Exists(alt));
        d.Anlegen("neu", new Tresor());
        Assert.NotNull(TresorDatei.Importieren(alt, "vergessen"));
    }
}

public class IbanTests
{
    [Theory]
    [InlineData("DE89 3704 0044 0532 0130 00")]
    [InlineData("de89370400440532013000")]
    [InlineData("FR14 2004 1010 0505 0001 3M02 606")]
    [InlineData("AT61 1904 3002 3457 3201")]
    public void GueltigeIbans(string iban) => Assert.Null(Iban.Pruefen(iban));

    [Fact]
    public void FalschePruefziffer() => Assert.Contains("Prüfziffer", Iban.Pruefen("DE88 3704 0044 0532 0130 00"));

    [Fact]
    public void FalscheLaenge() => Assert.Contains("22 Zeichen", Iban.Pruefen("DE89 3704 0044 0532 0130"));

    [Fact]
    public void Formatieren_Maskieren_Blz()
    {
        Assert.Equal("DE89 3704 0044 0532 0130 00", Iban.Formatieren("de89370400440532013000"));
        Assert.Equal("DE89 •••• •••• 3000", Iban.Maskieren("DE89370400440532013000"));
        Assert.Equal("37040044", Iban.Bankleitzahl("DE89370400440532013000"));
        Assert.Null(Iban.Pruefen(""));
    }
}

public class FinanzenTests
{
    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234,5", 1234.5)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("-50 €", -50)]
    [InlineData("1.000", 1000)]
    [InlineData("12.5", 12.5)]
    [InlineData("−7,10", -7.10)]
    public void BetragLesen(string text, double erwartet)
    {
        Assert.True(Finanzen.BetragLesen(text, out var b));
        Assert.Equal((decimal)erwartet, b);
    }

    [Fact]
    public void BetragLesen_Leer() => Assert.False(Finanzen.BetragLesen("abc", out _));

    [Fact]
    public void Euro_Format() => Assert.Equal("1.234,50 €", Finanzen.Euro(1234.5m));

    [Fact]
    public void Summen_UndVerlauf()
    {
        var t = new Tresor { Banken = BankKatalog.StandardBanken() };
        var a = new Konto { Name = "Giro" };
        var b = new Konto { Name = "Klarna Ratenkauf", Art = KontoArt.Kredit };
        t.Banken[0].Konten.Add(a);
        t.Banken[5].Konten.Add(b);
        a.StandSetzen(1000, new DateTime(2026, 1, 1));
        b.StandSetzen(-200, new DateTime(2026, 1, 10));
        a.StandSetzen(1500, new DateTime(2026, 2, 1));
        a.StandSetzen(1600, new DateTime(2026, 2, 1, 18, 0, 0));

        Assert.Equal(1600, Finanzen.Vermoegen(t));
        Assert.Equal(200, Finanzen.Schulden(t));
        Assert.Equal(1400, Finanzen.Netto(t));
        Assert.Equal(2, a.Verlauf.Count);
        var v = Finanzen.Verlauf(t);
        Assert.Equal([1000m, 800m, 1400m], v.Select(x => x.Summe).ToArray());
        Assert.Equal([800m, 1400m], Finanzen.Verlauf(t, new DateTime(2026, 1, 15)).Select(x => x.Summe).ToArray());
    }

    [Fact]
    public void Zahlungen_ProMonat()
    {
        var t = new Tresor();
        t.Zahlungen.Add(new Zahlung { Name = "Gehalt", Betrag = 2500, IstEinnahme = true });
        t.Zahlungen.Add(new Zahlung { Name = "Versicherung", Betrag = 120, Rhythmus = Rhythmus.Jaehrlich });
        t.Zahlungen.Add(new Zahlung { Name = "Handy", Betrag = 30 });
        t.Zahlungen.Add(new Zahlung { Name = "Alt", Betrag = 99, Aktiv = false });
        Assert.Equal(2500, Finanzen.EinnahmenProMonat(t));
        Assert.Equal(40, Finanzen.AusgabenProMonat(t));
    }

    [Fact]
    public void Hinweise_KarteLaeuftAb()
    {
        var t = new Tresor { Banken = BankKatalog.StandardBanken() };
        var k = new Konto { Name = "Giro" };
        k.Karten.Add(new Karte { Bezeichnung = "Visa", GueltigMonat = 12, GueltigJahr = 2026 });
        k.Karten.Add(new Karte { Bezeichnung = "Alt", GueltigMonat = 1, GueltigJahr = 2025 });
        t.Banken[0].Konten.Add(k);
        var h = Finanzen.Hinweise(t, new DateTime(2026, 11, 14));
        Assert.Contains(h, x => x.Text.Contains("Visa") && x.Text.Contains("läuft 12/26 ab"));
        Assert.Contains(h, x => x.Text.Contains("Alt") && x.Text.Contains("abgelaufen"));
    }

    [Fact]
    public void Katalog_EnthaeltDominiksBanken()
    {
        var banken = BankKatalog.StandardBanken();
        Assert.Equal(["ING-DiBa", "Crédit Agricole", "CosmosDirekt", "Bank11", "Sparkasse", "Klarna"], banken.Select(b => b.Name).ToArray());
        Assert.All(banken, b => Assert.StartsWith("https://", b.Website));
        Assert.Equal("ING-DiBa", BankKatalog.Finden("ing diba")?.Name);
        Assert.Equal("Crédit Agricole", BankKatalog.Finden("Credit Agricole")?.Name);
        Assert.Equal("CosmosDirekt", BankKatalog.Finden("cosmos direkt")?.Name);
    }

    [Fact]
    public void Csv_OhnePasswoerter()
    {
        var t = new Tresor { Banken = BankKatalog.StandardBanken() };
        t.Banken[0].Passwort = "supergeheim";
        t.Banken[0].Konten.Add(new Konto { Name = "=Giro;1", Kontostand = 5.5m });
        var csv = CsvExport.Konten(t);
        Assert.DoesNotContain("supergeheim", csv);
        Assert.Contains("\"'=Giro;1\"", csv);
        Assert.Contains("5,50", csv);
    }
}

public class IbanBauTests
{
    [Fact]
    public void ErzeugteIbanIstGueltig()
    {
        Assert.Equal("DE89370400440532013000", IbanBau.Erzeugen("DE", "370400440532013000"));
        Assert.True(Iban.IstGueltig(IbanBau.Erzeugen("FR", "30006000011234567890189")));
    }
}
