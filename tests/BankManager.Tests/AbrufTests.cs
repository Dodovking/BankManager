using BankManager.Banking;
using BankManager.Core;
using Xunit;

namespace BankManager.Tests;

public class AbrufTests
{
    [Fact]
    public void Kontonummer_AusDeutscherIban()
    {
        Assert.Equal("532013000", KontoAbruf.KontonummerAus("DE89 3704 0044 0532 0130 00"));
        Assert.Null(KontoAbruf.KontonummerAus("FR1420041010050500013M02606"));
    }

    [Fact]
    public async Task OhneProduktnummer_KeinVerbindungsversuch()
    {
        var bank = new Bank { Name = "ING-DiBa", FinTs = new FinTsZugang { Aktiv = true, Url = "https://example.invalid/fints", Benutzerkennung = "x" } };
        var konto = new Konto { Iban = "DE89370400440532013000" };
        var r = await new KontoAbruf("", "1.0.0").SaldoAbrufen(bank, konto, "pin", _ => Task.FromResult<string?>(null), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("Produktnummer", r.Meldung);
    }

    [Fact]
    public void FinTsZugang_WirdImTresorGespeichert()
    {
        using var dir = new TempDir();
        var pfad = Path.Combine(dir.Pfad, "t.bmtresor");
        var t = new Tresor { Banken = [new Bank { Name = "ING", FinTs = new FinTsZugang { Aktiv = true, Url = "https://fints.example", Pin = "1234", PinMerken = true } }] };
        new TresorDatei(pfad, 2000).Anlegen("pw", t);
        var g = new TresorDatei(pfad).Oeffnen("pw");
        Assert.True(g.Banken[0].AutoAbruf);
        Assert.Equal("1234", g.Banken[0].FinTs!.Pin);
        Assert.DoesNotContain("fints.example", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(pfad)));
    }
}
