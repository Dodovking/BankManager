using BankManager.Core;

namespace BankManager.Gui;

/// <summary>
/// "--screenshots &lt;ordner&gt;": renders every page and dialog with demo data into PNG files, in a throw-away data folder.
/// Used to check the layout on a real Windows PC without touching the user's vault.
/// </summary>
internal static class Bildschirmfotos
{
    public static int Erstellen(string ordner)
    {
        Directory.CreateDirectory(ordner);
        var log = new List<string>();
        var tmp = Path.Combine(Path.GetTempPath(), "BankManager-Fotos-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            foreach (var design in new[] { ThemeChoice.Dunkel, ThemeChoice.Hell })
            {
                Theme.Set(design);
                string p = design == ThemeChoice.Dunkel ? "" : "hell-";

                var leer = new Sitzung(Path.Combine(tmp, p + "leer"));
                using (var f = Fenster(leer))
                {
                    Foto(f, ordner, p + "01-erster-start", log);
                    f.Close();
                }

                var s = new Sitzung(Path.Combine(tmp, p + "demo"));
                var t = Demo();
                s.Datei.Anlegen("demo1234", t);
                s.Datei.Sperren();
                using var form = Fenster(s);
                Foto(form, ordner, p + "02-gesperrt", log);
                form.Entsperren(s.Datei.Oeffnen("demo1234"));
                Foto(form, ordner, p + "03-uebersicht", log);
                if (design == ThemeChoice.Hell) { form.Close(); continue; }
                form.Zeige(form.Banken);
                Foto(form, ordner, "04-banken", log);
                form.BankOeffnen(s.T.Banken[0]);
                Foto(form, ordner, "05-bank-ing", log);
                form.BankOeffnen(s.T.Banken[5]);
                Foto(form, ordner, "06-bank-klarna", log);
                form.Zeige(form.Konten);
                Foto(form, ordner, "07-konten", log);
                form.Zeige(form.Karten);
                Foto(form, ordner, "08-karten", log);
                form.Zeige(form.Zahlungen);
                Foto(form, ordner, "09-zahlungen", log);
                form.Zeige(form.EinstellungenSeite);
                Foto(form, ordner, "10-einstellungen", log);
                s.Einstellungen.BetraegeVerbergen = true;
                form.Zeige(form.Uebersicht);
                Foto(form, ordner, "11-verborgen", log);
                s.Einstellungen.BetraegeVerbergen = false;

                var ing = s.T.Banken[0];
                var giro = ing.Konten[0];
                Dialog(new BankDialog(new Bank(), neu: true), ordner, "20-dialog-bank-neu", log);
                Dialog(new BankDialog(ing, neu: false), ordner, "21-dialog-bank", log);
                Dialog(new KontoDialog(s.T, giro, ing, neu: false), ordner, "22-dialog-konto", log);
                Dialog(new StandDialog(s, ing, giro), ordner, "23-dialog-kontostand", log);
                Dialog(new KarteDialog(s.T, giro.Karten[0], giro, neu: false), ordner, "24-dialog-karte", log);
                Dialog(new ZahlungDialog(s.T, s.T.Zahlungen[1], neu: false), ordner, "25-dialog-zahlung", log);
                Dialog(new PasswortDialog(s), ordner, "26-dialog-passwort", log);
                Dialog(new UpdateForm(new UpdateChecker(releaseUrl: "http://127.0.0.1:9/keine-verbindung"), null), ordner, "27-dialog-update", log);
                form.Close();
            }
            log.Add("OK");
            return 0;
        }
        catch (Exception e)
        {
            log.Add("FEHLER: " + e);
            return 1;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(ordner, "protokoll.txt"), log);
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    private static MainForm Fenster(Sitzung s)
    {
        var f = new MainForm(s, "http://127.0.0.1:9/keine-verbindung")
        {
            Fotomodus = true,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ShowInTaskbar = false,
        };
        f.WindowState = FormWindowState.Normal;
        f.Size = new Size(Ui.S(1320), Ui.S(860));
        f.Show();
        Pumpen();
        return f;
    }

    private static void Pumpen()
    {
        for (int i = 0; i < 6; i++) { Application.DoEvents(); Thread.Sleep(30); }
    }

    private static void Foto(Control c, string ordner, string name, List<string> log)
    {
        Pumpen();
        c.PerformLayout();
        c.Refresh();
        Pumpen();
        using var bmp = new Bitmap(c.Width, c.Height);
        c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
        bmp.Save(Path.Combine(ordner, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        log.Add(name);
    }

    private static void Dialog(Form d, string ordner, string name, List<string> log)
    {
        using (d)
        {
            d.StartPosition = FormStartPosition.Manual;
            d.Location = new Point(-20000, -20000);
            d.Show();
            Theme.ApplyTitleBar(d);
            Pumpen();
            Foto(d, ordner, name, log);
            d.Close();
        }
    }

    public static Tresor Demo()
    {
        var heute = DateTime.Today;
        var t = new Tresor { Banken = BankKatalog.StandardBanken() };
        var (ing, ca, cosmos, bank11, spk, klarna) = (t.Banken[0], t.Banken[1], t.Banken[2], t.Banken[3], t.Banken[4], t.Banken[5]);
        ing.Anmeldename = "1234567890";
        ing.Passwort = "Demo-Passwort!";
        ing.Hotline = "Hotline aus der Banking-App";
        ing.Bic = "INGDDEFFXXX";

        Konto K(Bank b, string name, KontoArt art, string iban, decimal start, decimal schritt, int monate = 8)
        {
            var k = new Konto { Name = name, Art = art, Iban = iban, Inhaber = "Max Mustermann" };
            var rnd = new Random(name.Length * 31 + monate);
            decimal wert = start;
            for (int m = monate; m >= 0; m--)
            {
                k.StandSetzen(Math.Round(wert, 2), heute.AddMonths(-m).AddDays(-rnd.Next(0, 5)));
                wert += schritt + rnd.Next(-120, 120);
            }
            b.Konten.Add(k);
            return k;
        }

        var giro = K(ing, "Girokonto", KontoArt.Girokonto, IbanBau.Erzeugen("DE", "500105170123456789"), 1450, 110);
        giro.Dispo = 1000;
        giro.Karten.Add(new Karte { Bezeichnung = "Visa Debitkarte", Typ = KartenTyp.Debitkarte, Netzwerk = "Visa", LetzteZiffern = "4711", GueltigMonat = 9, GueltigJahr = heute.Year + 3 });
        giro.Karten.Add(new Karte { Bezeichnung = "Girocard", Typ = KartenTyp.Girocard, Netzwerk = "Girocard", LetzteZiffern = "0815", GueltigMonat = heute.AddMonths(1).Month, GueltigJahr = heute.AddMonths(1).Year });
        var extra = K(ing, "Extra-Konto", KontoArt.Tagesgeld, IbanBau.Erzeugen("DE", "500105175544332211"), 6200, 260);
        extra.Zinssatz = 2.5m;
        K(ing, "Depot", KontoArt.Depot, "", 3100, 140);
        K(ca, "Compte courant", KontoArt.Girokonto, IbanBau.Erzeugen("FR", "30006000011234567890189"), 520, 15, 4);
        var cd = K(cosmos, "Tagesgeld", KontoArt.Tagesgeld, IbanBau.Erzeugen("DE", "590500000098765432"), 2500, 60, 6);
        cd.Zinssatz = 2.1m;
        var fg = new Konto { Name = "Festgeld 12 Monate", Art = KontoArt.Festgeld, Zinssatz = 3.0m, LaufzeitBis = heute.AddDays(21) };
        fg.StandSetzen(5000, heute.AddMonths(-11));
        bank11.Konten.Add(fg);
        var spGiro = K(spk, "Girokonto", KontoArt.Girokonto, IbanBau.Erzeugen("DE", "123500000012345678"), 300, -20, 5);
        spGiro.Karten.Add(new Karte { Bezeichnung = "Sparkassen-Card", Typ = KartenTyp.Girocard, Netzwerk = "Girocard", LetzteZiffern = "2233", GueltigMonat = 12, GueltigJahr = heute.Year + 2 });
        var rate = new Konto { Name = "Ratenkauf", Art = KontoArt.Kredit, Notizen = "Fernseher, 12 Raten" };
        rate.StandSetzen(-583.30m, heute.AddMonths(-2));
        rate.StandSetzen(-466.64m, heute.AddDays(-10));
        klarna.Konten.Add(rate);
        var kk = new Konto { Name = "Klarna Card", Art = KontoArt.Kreditkarte };
        kk.StandSetzen(-42.90m, heute.AddDays(-3));
        kk.Karten.Add(new Karte { Bezeichnung = "Klarna Card", Typ = KartenTyp.Kreditkarte, Netzwerk = "Visa", LetzteZiffern = "9001", GueltigMonat = 3, GueltigJahr = heute.Year + 4, Limit = 1500 });
        klarna.Konten.Add(kk);

        t.Zahlungen.AddRange(
        [
            new Zahlung { Name = "Gehalt", Betrag = 2650, IstEinnahme = true, Tag = 28, KontoId = giro.Id, Kategorie = "Gehalt" },
            new Zahlung { Name = "Miete", Betrag = 780, Tag = 1, KontoId = giro.Id, Kategorie = "Wohnen" },
            new Zahlung { Name = "Strom", Betrag = 64, Tag = 15, KontoId = giro.Id, Kategorie = "Wohnen" },
            new Zahlung { Name = "Handyvertrag", Betrag = 29.99m, Tag = 5, KontoId = giro.Id, Kategorie = "Handy / Internet" },
            new Zahlung { Name = "Klarna Rate Fernseher", Betrag = 58.33m, Tag = 10, KontoId = giro.Id, Kategorie = "Kredit / Rate" },
            new Zahlung { Name = "Sparplan Tagesgeld", Betrag = 200, Tag = 1, KontoId = cd.Id, Kategorie = "Sparen" },
            new Zahlung { Name = "Haftpflicht", Betrag = 59.90m, Rhythmus = Rhythmus.Jaehrlich, KontoId = spGiro.Id, Kategorie = "Versicherung" },
            new Zahlung { Name = "Netflix", Betrag = 13.99m, Tag = 20, KontoId = kk.Id, Kategorie = "Abo" },
        ]);
        return t;
    }
}
