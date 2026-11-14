using System.Globalization;
using BankManager.Core;

namespace BankManager.Gui;

/// <summary>Shared helpers for the edit dialogs: labelled fields and validation.</summary>
public class EditDialog : DialogBase
{
    protected static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public EditDialog(string title, string okText, int width, int height) : base(title, okText, width, height)
    {
        OkButton.Click += (_, _) =>
        {
            var fehler = Pruefen();
            if (fehler != null)
            {
                MessageBox.Show(this, fehler, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Uebernehmen();
            DialogResult = DialogResult.OK;
            Close();
        };
        AcceptButton = null;
    }

    protected virtual string? Pruefen() => null;
    protected virtual void Uebernehmen() { }

    protected InputBox Feld(string titel, string wert = "", bool multiline = false, string? hinweis = null)
    {
        Place(Make.Label(titel, Ui.Bold), Ui.S(18), 4);
        var box = new InputBox(multiline) { Width = ContentWidth, Text = wert };
        Place(box, box.Height, hinweis == null ? 12 : 4);
        if (hinweis != null) { AddHint(hinweis); Y += Ui.S(4); }
        return box;
    }

    protected AuswahlFeld Auswahl(string titel, bool bearbeitbar = false, int width = 0)
    {
        Place(Make.Label(titel, Ui.Bold), Ui.S(18), 4);
        var a = new AuswahlFeld(bearbeitbar) { Width = width > 0 ? width : ContentWidth };
        Place(a, a.Height, 12);
        return a;
    }

    /// <summary>Two fields side by side.</summary>
    protected (T1 A, T2 B) Paar<T1, T2>(string titelA, T1 a, string titelB, T2 b) where T1 : Control where T2 : Control
    {
        int half = (ContentWidth - Ui.S(12)) / 2;
        var la = Make.Label(titelA, Ui.Bold);
        var lb = Make.Label(titelB, Ui.Bold);
        la.Location = new Point(Left0, Y);
        lb.Location = new Point(Left0 + half + Ui.S(12), Y);
        Body.Controls.Add(la);
        Body.Controls.Add(lb);
        Y += Ui.S(22);
        a.Width = half;
        b.Width = half;
        a.Location = new Point(Left0, Y);
        b.Location = new Point(Left0 + half + Ui.S(12), Y);
        Body.Controls.Add(a);
        Body.Controls.Add(b);
        Y += Math.Max(a.Height, b.Height) + Ui.S(12);
        return (a, b);
    }

    protected static string Zahl(decimal? d, string format = "0.00") => d?.ToString(format, De) ?? "";

    protected static bool OptionalBetrag(InputBox box, out decimal? wert)
    {
        wert = null;
        if (string.IsNullOrWhiteSpace(box.Text)) return true;
        if (!Finanzen.BetragLesen(box.Text, out var w)) return false;
        wert = w;
        return true;
    }

    protected static void NurZiffern(InputBox box, int max)
    {
        box.Box.MaxLength = max;
        box.Box.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
    }
}

public sealed class BankDialog : EditDialog
{
    private readonly Bank _bank;
    private readonly InputBox _name, _kuerzel, _website, _bic, _hotline, _login, _pw, _kontakt, _notizen;
    private readonly FarbFeld _farbe;
    private Color _farbWert;

    public BankDialog(Bank bank, bool neu) : base(neu ? "Bank hinzufügen" : "Bank bearbeiten", neu ? "Hinzufügen" : "Speichern", 560, 640)
    {
        _bank = bank;
        _farbWert = Farben.Lesen(bank.Farbe, Theme.P.Accent);
        if (neu)
        {
            var vorlage = Auswahl("Vorlage (optional)");
            vorlage.Hinzufuegen("", "– eigene Bank –");
            foreach (var v in BankKatalog.Alle) vorlage.Hinzufuegen(v, v.Name + (v.Hinweis.Length > 0 ? "  ·  " + v.Hinweis : ""));
            vorlage.Index = 0;
            vorlage.AuswahlGeaendert += (_, _) =>
            {
                if (vorlage.Wert is not BankVorlage v) return;
                _name!.Text = v.Name;
                _kuerzel!.Text = v.Kuerzel;
                _website!.Text = v.Website;
                FarbeSetzen(Farben.Lesen(v.Farbe, Theme.P.Accent));
            };
        }
        _name = Feld("Name der Bank", bank.Name);
        _kuerzel = new InputBox { Text = bank.Kuerzel };
        _kuerzel.Box.MaxLength = 4;
        _farbe = new FarbFeld(_farbWert) { Height = _kuerzel.Height };
        _farbe.Click += (_, _) =>
        {
            using var cd = new ColorDialog { Color = _farbWert, FullOpen = true };
            if (cd.ShowDialog(this) == DialogResult.OK) FarbeSetzen(cd.Color);
        };
        Paar("Logo-Kürzel (max. 4 Zeichen)", _kuerzel, "Farbe (zum Ändern klicken)", _farbe);
        _website = Feld("Online-Banking-Adresse", bank.Website);
        (_bic, _hotline) = Paar("BIC", new InputBox { Text = bank.Bic }, "Hotline / Telefon", new InputBox { Text = bank.Hotline });
        AddHeading("Zugangsdaten (verschlüsselt gespeichert)");
        _login = Feld("Anmeldename / Kundennummer", bank.Anmeldename);
        _pw = Feld("Passwort / Zugangsnummer (optional)", bank.Passwort, hinweis: "Wird nur verschlüsselt in deinem Tresor gespeichert. PINs und TANs bitte nie speichern.");
        _pw.Box.UseSystemPasswordChar = true;
        var zeigen = new ToggleSwitch("Passwort anzeigen");
        zeigen.CheckedChanged += (_, _) => _pw.Box.UseSystemPasswordChar = !zeigen.Checked;
        Place(zeigen, zeigen.Height, 10);
        _kontakt = Feld("Ansprechpartner / Filiale", bank.Ansprechpartner);
        _notizen = Feld("Notizen", bank.Notizen, multiline: true);
        FinishLayout();
    }

    private void FarbeSetzen(Color c)
    {
        _farbWert = c;
        _farbe.Farbe = c;
    }

    protected override string? Pruefen() => _name.Text.Trim().Length == 0 ? "Bitte einen Namen für die Bank eingeben." : null;

    protected override void Uebernehmen()
    {
        _bank.Name = _name.Text.Trim();
        _bank.Kuerzel = _kuerzel.Text.Trim();
        _bank.Farbe = Farben.Hex(_farbWert);
        var web = _website.Text.Trim();
        if (web.Length > 0 && !web.Contains("://")) web = "https://" + web;
        _bank.Website = web;
        _bank.Bic = _bic.Text.Trim().ToUpperInvariant();
        _bank.Hotline = _hotline.Text.Trim();
        _bank.Anmeldename = _login.Text.Trim();
        _bank.Passwort = _pw.Text;
        _bank.Ansprechpartner = _kontakt.Text.Trim();
        _bank.Notizen = _notizen.Text.Trim();
    }
}

public sealed class KontoDialog : EditDialog
{
    private readonly Konto _konto;
    private readonly AuswahlFeld? _bankWahl;
    private readonly InputBox _name, _iban, _inhaber, _stand, _zins, _dispo, _laufzeit, _notizen;
    private readonly AuswahlFeld _art, _waehrung;
    private readonly Label _ibanInfo;
    private readonly bool _neu;

    public Bank? GewaehlteBank => _bankWahl?.Wert as Bank;

    public KontoDialog(Tresor t, Konto konto, Bank? bank, bool neu) : base(neu ? "Konto hinzufügen" : "Konto bearbeiten", neu ? "Hinzufügen" : "Speichern", 560, 660)
    {
        _konto = konto;
        _neu = neu;
        if (bank == null)
        {
            _bankWahl = Auswahl("Bank");
            foreach (var b in t.Banken) _bankWahl.Hinzufuegen(b, b.Name);
            _bankWahl.Index = 0;
        }
        _name = Feld("Bezeichnung", konto.Name);
        _art = new AuswahlFeld();
        foreach (var a in Enum.GetValues<KontoArt>()) _art.Hinzufuegen(a, Texte.Name(a));
        _art.Wert = konto.Art;
        _inhaber = new InputBox { Text = konto.Inhaber };
        Paar("Kontoart", _art, "Kontoinhaber", _inhaber);
        if (neu && _name.Text.Length == 0)
            _art.AuswahlGeaendert += (_, _) => { if (_name.Text.Length == 0 || Enum.GetValues<KontoArt>().Any(a => Texte.Name(a) == _name.Text)) _name.Text = Texte.Name((KontoArt)_art.Wert!); };
        _iban = Feld("IBAN", Iban.Formatieren(konto.Iban));
        _ibanInfo = Make.Label("", Ui.Small, width: ContentWidth);
        _ibanInfo.Tag = "keepcolor";
        Place(_ibanInfo, Ui.S(16), 8);
        _iban.Box.TextChanged += (_, _) => IbanPruefen();
        _stand = new InputBox { Text = neu ? "" : Zahl(konto.Kontostand) };
        _waehrung = new AuswahlFeld(bearbeitbar: true) { Text = konto.Waehrung };
        foreach (var w in new[] { "EUR", "USD", "GBP", "CHF", "PLN" }) _waehrung.Hinzufuegen(w, w);
        Paar(neu ? "Aktueller Kontostand" : "Kontostand", _stand, "Währung", _waehrung);
        AddHint("Schulden (z. B. Kredit, Ratenkauf, offene Kreditkarte) als Minusbetrag eintragen, z. B. -1.250,00.");
        Y += Ui.S(6);
        (_zins, _dispo) = Paar("Zinssatz in % (optional)", new InputBox { Text = Zahl(konto.Zinssatz, "0.###") }, "Dispo-Rahmen (optional)", new InputBox { Text = Zahl(konto.Dispo) });
        _laufzeit = Feld("Laufzeit bis (optional, TT.MM.JJJJ)", konto.LaufzeitBis?.ToString("dd.MM.yyyy") ?? "", hinweis: "Für Festgeld, Kredite oder Verträge – du bekommst 30 Tage vorher einen Hinweis.");
        _notizen = Feld("Notizen", konto.Notizen, multiline: true);
        FinishLayout();
        IbanPruefen();
    }

    private void IbanPruefen()
    {
        var fehler = Iban.Pruefen(_iban.Text);
        if (Iban.Normalisieren(_iban.Text).Length == 0) { _ibanInfo.Text = ""; return; }
        _ibanInfo.ForeColor = fehler == null ? Theme.P.Success : Theme.P.Warning;
        var blz = Iban.Bankleitzahl(_iban.Text);
        _ibanInfo.Text = fehler ?? ("✓ IBAN ist gültig" + (blz != null ? $"  ·  Bankleitzahl {blz}" : ""));
    }

    protected override string? Pruefen()
    {
        if (_bankWahl != null && _bankWahl.Wert == null) return "Bitte zuerst eine Bank anlegen.";
        if (_name.Text.Trim().Length == 0) return "Bitte eine Bezeichnung eingeben, z. B. „Girokonto“.";
        var ibanFehler = Iban.Pruefen(_iban.Text);
        if (ibanFehler != null && MessageBox.Show(this, ibanFehler + "\n\nTrotzdem so speichern?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return "Bitte die IBAN korrigieren.";
        if (_stand.Text.Trim().Length > 0 && !Finanzen.BetragLesen(_stand.Text, out _)) return "Der Kontostand ist keine gültige Zahl.";
        if (!OptionalBetrag(_zins, out _)) return "Der Zinssatz ist keine gültige Zahl.";
        if (!OptionalBetrag(_dispo, out _)) return "Der Dispo ist keine gültige Zahl.";
        if (_laufzeit.Text.Trim().Length > 0 && !Finanzen.DatumLesen(_laufzeit.Text, out _)) return "Das Datum bitte als TT.MM.JJJJ eingeben.";
        return null;
    }

    protected override void Uebernehmen()
    {
        _konto.Name = _name.Text.Trim();
        _konto.Art = (KontoArt)(_art.Wert ?? KontoArt.Sonstiges);
        _konto.Iban = Iban.Normalisieren(_iban.Text);
        _konto.Inhaber = _inhaber.Text.Trim();
        _konto.Waehrung = _waehrung.Text.Trim().ToUpperInvariant() is { Length: > 0 } w ? w : "EUR";
        if (Finanzen.BetragLesen(_stand.Text, out var stand) && (_neu || stand != _konto.Kontostand)) _konto.StandSetzen(stand);
        OptionalBetrag(_zins, out var zins);
        OptionalBetrag(_dispo, out var dispo);
        _konto.Zinssatz = zins;
        _konto.Dispo = dispo is decimal d ? Math.Abs(d) : null;
        _konto.LaufzeitBis = Finanzen.DatumLesen(_laufzeit.Text, out var lz) ? lz : null;
        _konto.Notizen = _notizen.Text.Trim();
    }
}

public sealed class StandDialog : EditDialog
{
    private readonly Konto _konto;
    private readonly InputBox _betrag, _datum;

    public StandDialog(Sitzung s, Bank bank, Konto konto) : base("Kontostand eintragen", "Eintragen", 480, 420)
    {
        _konto = konto;
        AddHeading($"{konto.Name} · {bank.Name}");
        AddHint(konto.StandVom is DateTime vom ? $"Bisher: {s.Geld(konto.Kontostand, konto.Waehrung)} (Stand {vom:dd.MM.yyyy})" : "Noch kein Kontostand eingetragen.");
        Y += Ui.S(8);
        _betrag = Feld($"Neuer Kontostand in {konto.Waehrung}", Zahl(konto.Kontostand), hinweis: "Minusbetrag für Schulden, z. B. -350,00");
        _betrag.Box.Font = Ui.Section;
        _datum = Feld("Stand vom (TT.MM.JJJJ)", DateTime.Today.ToString("dd.MM.yyyy"));
        var letzte = konto.Verlauf.AsEnumerable().Reverse().Take(4).ToList();
        if (letzte.Count > 0)
        {
            AddHeading("Letzte Einträge");
            foreach (var st in letzte) AddHint($"{st.Datum:dd.MM.yyyy}   {s.Geld(st.Betrag, konto.Waehrung)}");
        }
        FinishLayout();
        Shown += (_, _) => { _betrag.Box.Focus(); _betrag.Box.SelectAll(); };
        AcceptButton = OkButton;
    }

    protected override string? Pruefen()
    {
        if (!Finanzen.BetragLesen(_betrag.Text, out _)) return "Bitte einen gültigen Betrag eingeben, z. B. 1.234,56";
        if (!Finanzen.DatumLesen(_datum.Text, out var d)) return "Das Datum bitte als TT.MM.JJJJ eingeben.";
        if (d.Date > DateTime.Today) return "Das Datum liegt in der Zukunft.";
        return null;
    }

    protected override void Uebernehmen()
    {
        Finanzen.BetragLesen(_betrag.Text, out var b);
        Finanzen.DatumLesen(_datum.Text, out var d);
        var wann = d.Date == DateTime.Today ? DateTime.Now : d.Date.AddHours(12);
        if (_konto.StandVom is DateTime vorher && wann < vorher)
        {
            _konto.Verlauf.RemoveAll(x => x.Datum.Date == wann.Date);
            _konto.Verlauf.Add(new Stand { Datum = wann, Betrag = b });
            _konto.Verlauf.Sort((x, y) => x.Datum.CompareTo(y.Datum));
        }
        else _konto.StandSetzen(b, wann);
    }
}

public sealed class KarteDialog : EditDialog
{
    private readonly Karte _karte;
    private readonly AuswahlFeld _konto, _typ, _netz;
    private readonly InputBox _bez, _ziffern, _monat, _jahr, _limit, _notizen;

    public Konto? GewaehltesKonto => _konto.Wert as Konto;

    public KarteDialog(Tresor t, Karte karte, Konto? konto, bool neu) : base(neu ? "Karte hinzufügen" : "Karte bearbeiten", neu ? "Hinzufügen" : "Speichern", 540, 600)
    {
        _karte = karte;
        _konto = Auswahl("Gehört zu Konto");
        foreach (var b in t.Banken)
        foreach (var k in b.Konten) _konto.Hinzufuegen(k, $"{b.Name} – {k.Name}");
        if (konto != null) _konto.Wert = konto; else _konto.Index = 0;
        _bez = Feld("Bezeichnung", karte.Bezeichnung, hinweis: "z. B. „Visa Card“, „Girocard“, „Klarna Card“");
        _typ = new AuswahlFeld();
        foreach (var x in Enum.GetValues<KartenTyp>()) _typ.Hinzufuegen(x, Texte.Name(x));
        _typ.Wert = karte.Typ;
        _netz = new AuswahlFeld(bearbeitbar: true) { Text = karte.Netzwerk };
        foreach (var n in new[] { "Visa", "Mastercard", "Girocard", "Maestro", "V Pay", "American Express" }) _netz.Hinzufuegen(n, n);
        Paar("Kartenart", _typ, "Netzwerk", _netz);
        _ziffern = Feld("Letzte 4 Ziffern der Kartennummer", karte.LetzteZiffern, hinweis: "Aus Sicherheitsgründen nur die letzten 4 Ziffern – nie die ganze Nummer, CVC oder PIN.");
        NurZiffern(_ziffern, 4);
        _monat = new InputBox { Text = karte.GueltigMonat?.ToString("00") ?? "" };
        _jahr = new InputBox { Text = karte.GueltigJahr?.ToString() ?? "" };
        NurZiffern(_monat, 2);
        NurZiffern(_jahr, 4);
        Paar("Gültig bis Monat (MM)", _monat, "Jahr (JJJJ)", _jahr);
        _limit = Feld("Verfügungsrahmen / Limit (optional)", Zahl(karte.Limit));
        _notizen = Feld("Notizen", karte.Notizen, multiline: true);
        FinishLayout();
    }

    protected override string? Pruefen()
    {
        if (_konto.Wert == null) return "Lege zuerst ein Konto an, zu dem die Karte gehört.";
        if (_ziffern.Text.Length is > 0 and < 4) return "Bitte genau 4 Ziffern eingeben (oder leer lassen).";
        bool m = _monat.Text.Length > 0, j = _jahr.Text.Length > 0;
        if (m != j) return "Bitte Monat und Jahr angeben (oder beides leer lassen).";
        if (m && (!int.TryParse(_monat.Text, out var mm) || mm is < 1 or > 12)) return "Der Monat muss zwischen 01 und 12 liegen.";
        if (j && (!int.TryParse(_jahr.Text, out var jj) || !(jj is >= 0 and <= 99 || jj is >= 2000 and <= 2100))) return "Das Jahr bitte als JJJJ eingeben, z. B. 2029.";
        if (!OptionalBetrag(_limit, out _)) return "Das Limit ist keine gültige Zahl.";
        return null;
    }

    protected override void Uebernehmen()
    {
        _karte.Typ = (KartenTyp)(_typ.Wert ?? KartenTyp.Sonstige);
        _karte.Bezeichnung = _bez.Text.Trim().Length > 0 ? _bez.Text.Trim() : Texte.Name(_karte.Typ);
        _karte.Netzwerk = _netz.Text.Trim();
        _karte.LetzteZiffern = _ziffern.Text;
        _karte.GueltigMonat = int.TryParse(_monat.Text, out var mm) ? mm : null;
        _karte.GueltigJahr = int.TryParse(_jahr.Text, out var jj) ? (jj < 100 ? 2000 + jj : jj) : null;
        OptionalBetrag(_limit, out var limit);
        _karte.Limit = limit;
        _karte.Notizen = _notizen.Text.Trim();
    }
}

public sealed class ZahlungDialog : EditDialog
{
    private readonly Zahlung _z;
    private readonly InputBox _name, _betrag, _notizen;
    private readonly NumberBox _tag;
    private readonly Segmented _richtung;
    private readonly AuswahlFeld _rhythmus, _konto, _kategorie;
    private readonly ToggleSwitch _aktiv;

    public ZahlungDialog(Tresor t, Zahlung z, bool neu) : base(neu ? "Regelmäßige Zahlung hinzufügen" : "Zahlung bearbeiten", neu ? "Hinzufügen" : "Speichern", 540, 600)
    {
        _z = z;
        _richtung = new Segmented("Ausgabe", "Einnahme") { SelectedIndex = z.IstEinnahme ? 1 : 0 };
        Place(_richtung, _richtung.Height, 12);
        _name = Feld("Bezeichnung", z.Name, hinweis: "z. B. Miete, Handyvertrag, Netflix, Gehalt, Sparplan");
        _betrag = new InputBox { Text = neu ? "" : Zahl(z.Betrag) };
        _rhythmus = new AuswahlFeld();
        foreach (var r in Enum.GetValues<Rhythmus>()) _rhythmus.Hinzufuegen(r, Texte.Name(r));
        _rhythmus.Wert = z.Rhythmus;
        Paar("Betrag in €", _betrag, "Rhythmus", _rhythmus);
        _tag = new NumberBox(0, 31) { Value = z.Tag ?? 1 };
        _kategorie = new AuswahlFeld(bearbeitbar: true) { Text = z.Kategorie };
        foreach (var k in new[] { "Wohnen", "Versicherung", "Abo", "Handy / Internet", "Kredit / Rate", "Sparen", "Auto", "Gehalt", "Sonstiges" }) _kategorie.Hinzufuegen(k, k);
        Paar("Am Tag (1–31, 0 = egal)", _tag, "Kategorie", _kategorie);
        _konto = Auswahl("Von / auf Konto");
        _konto.Hinzufuegen("", "– kein bestimmtes Konto –");
        foreach (var b in t.Banken)
        foreach (var k in b.Konten) _konto.Hinzufuegen(k.Id, $"{b.Name} – {k.Name}");
        _konto.Wert = z.KontoId ?? "";
        if (_konto.Index < 0) _konto.Index = 0;
        _aktiv = new ToggleSwitch("Aktiv (zählt in den Fixkosten)") { Checked = z.Aktiv };
        Place(_aktiv, _aktiv.Height, 10);
        _notizen = Feld("Notizen", z.Notizen, multiline: true);
        FinishLayout();
    }

    protected override string? Pruefen()
    {
        if (_name.Text.Trim().Length == 0) return "Bitte eine Bezeichnung eingeben.";
        if (!Finanzen.BetragLesen(_betrag.Text, out var b) || b == 0) return "Bitte einen Betrag eingeben, z. B. 49,99";
        return null;
    }

    protected override void Uebernehmen()
    {
        Finanzen.BetragLesen(_betrag.Text, out var b);
        _z.Name = _name.Text.Trim();
        _z.Betrag = Math.Abs(b);
        _z.IstEinnahme = _richtung.SelectedIndex == 1;
        _z.Rhythmus = (Rhythmus)(_rhythmus.Wert ?? Rhythmus.Monatlich);
        _z.Tag = _tag.Value == 0 ? null : _tag.Value;
        _z.KontoId = _konto.Wert as string is { Length: > 0 } id ? id : null;
        _z.Kategorie = _kategorie.Text.Trim();
        _z.Aktiv = _aktiv.Checked;
        _z.Notizen = _notizen.Text.Trim();
    }
}

public sealed class PasswortDialog : EditDialog
{
    private readonly Sitzung _s;
    private readonly InputBox _alt, _neu, _neu2;

    public PasswortDialog(Sitzung s) : base("Master-Passwort ändern", "Ändern", 480, 420)
    {
        _s = s;
        _alt = Feld("Aktuelles Passwort");
        _neu = Feld("Neues Passwort", hinweis: "Mindestens 6 Zeichen.");
        _neu2 = Feld("Neues Passwort wiederholen");
        foreach (var b in new[] { _alt, _neu, _neu2 }) b.Box.UseSystemPasswordChar = true;
        FinishLayout();
    }

    protected override string? Pruefen()
    {
        if (_neu.Text.Length < 6) return "Das neue Passwort braucht mindestens 6 Zeichen.";
        if (_neu.Text != _neu2.Text) return "Die beiden neuen Passwörter sind nicht gleich.";
        try
        {
            Cursor = Cursors.WaitCursor;
            _s.Datei.PasswortAendern(_alt.Text, _neu.Text, _s.T);
        }
        catch (FalschesPasswortException) { return "Das aktuelle Passwort ist falsch."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "Speichern fehlgeschlagen: " + e.Message; }
        finally { Cursor = Cursors.Default; }
        return null;
    }
}

public sealed class PasswortAbfrage : EditDialog
{
    private readonly InputBox _pw;
    public string Passwort => _pw.Text;

    public PasswortAbfrage(string titel, string hinweis) : base(titel, "OK", 460, 260)
    {
        AddHint(hinweis);
        Y += Ui.S(8);
        _pw = Feld("Passwort");
        _pw.Box.UseSystemPasswordChar = true;
        FinishLayout();
        AcceptButton = OkButton;
    }

    protected override string? Pruefen() => _pw.Text.Length == 0 ? "Bitte das Passwort eingeben." : null;
}
