using BankManager.Core;

namespace BankManager.Gui;

/// <summary>First start: choose the master password. Later: unlock. Covers the whole window while locked.</summary>
public sealed class SperrSeite : Panel
{
    private readonly Sitzung _s;
    private readonly Card _karte = new();
    private readonly InputBox _pw = new();
    private readonly InputBox _pw2 = new();
    private readonly Label _fehler;
    private readonly FlatButton _ok;
    private readonly bool _neu;
    private int _fehlversuche;
    private bool _busy;

    public event Action<Tresor>? Entsperrt;

    public SperrSeite(Sitzung s)
    {
        _s = s;
        _neu = !s.Datei.Existiert;
        Dock = DockStyle.Fill;
        DoubleBuffered = true;
        Controls.Add(_karte);
        _karte.Size = new Size(Ui.S(440), Ui.S(_neu ? 470 : 330));

        int x = Ui.S(36), w = _karte.Width - Ui.S(72), y = Ui.S(32);
        var logo = new PictureBox { Image = Ui.AppIcon().ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(Ui.S(48), Ui.S(48)), Location = new Point(x, y), BackColor = Color.Transparent };
        _karte.Controls.Add(logo);
        var titel = Make.Label("Bank-Manager", Ui.Title);
        titel.Location = new Point(x + Ui.S(60), y - Ui.S(2));
        _karte.Controls.Add(titel);
        var unter = Make.Label(_neu ? "Willkommen! Lege dein Master-Passwort fest." : "Gesperrt – gib dein Master-Passwort ein.", Ui.Small, muted: true);
        unter.Location = new Point(x + Ui.S(62), y + Ui.S(30));
        _karte.Controls.Add(unter);
        y += Ui.S(78);

        void Feld(string text, InputBox box)
        {
            var l = Make.Label(text, Ui.Bold);
            l.Location = new Point(x, y);
            _karte.Controls.Add(l);
            y += Ui.S(24);
            box.Box.UseSystemPasswordChar = true;
            box.Location = new Point(x, y);
            box.Width = w;
            _karte.Controls.Add(box);
            y += box.Height + Ui.S(14);
        }

        Feld("Master-Passwort", _pw);
        if (_neu)
        {
            Feld("Passwort wiederholen", _pw2);
            var hinweis = Make.Label("Mit diesem Passwort werden alle deine Bankdaten verschlüsselt (AES-256) auf diesem PC gespeichert. " +
                                     "Merk es dir gut – ohne das Passwort kann niemand die Daten öffnen, auch nicht ich.", Ui.Small, muted: true, width: w);
            hinweis.Location = new Point(x, y);
            _karte.Controls.Add(hinweis);
            y += hinweis.PreferredHeight + Ui.S(12);
        }
        _fehler = Make.Label("", Ui.Small, width: w);
        _fehler.Tag = "keepcolor";
        _fehler.ForeColor = Theme.P.Danger;
        _fehler.Location = new Point(x, y);
        _karte.Controls.Add(_fehler);
        y += Ui.S(26);
        _ok = new FlatButton(_neu ? "Tresor anlegen" : "Entsperren", ButtonKind.Primary) { Width = w, Height = Ui.S(40), Location = new Point(x, y) };
        _karte.Controls.Add(_ok);
        _ok.Click += async (_, _) => await Los();
        foreach (var b in new[] { _pw, _pw2 })
            b.Box.KeyDown += async (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (_neu && b == _pw) _pw2.Box.Focus();
                else await Los();
            };
        Layout += (_, _) => _karte.Location = new Point(Math.Max(0, (Width - _karte.Width) / 2), Math.Max(0, (Height - _karte.Height) / 2 - Ui.S(20)));
    }

    public void Fokus() => _pw.Box.Focus();

    private async Task Los()
    {
        if (_busy) return;
        var pw = _pw.Text;
        _fehler.Text = "";
        if (_neu)
        {
            if (pw.Length < 6) { Fehler("Bitte mindestens 6 Zeichen verwenden."); return; }
            if (pw != _pw2.Text) { Fehler("Die beiden Passwörter sind nicht gleich."); return; }
        }
        else if (pw.Length == 0) { Fehler("Bitte das Master-Passwort eingeben."); return; }

        _busy = true;
        _ok.Enabled = false;
        _ok.Text = _neu ? "Lege an …" : "Entsperre …";
        try
        {
            Tresor daten;
            if (_neu)
            {
                daten = new Tresor { Banken = BankKatalog.StandardBanken() };
                await Task.Run(() => _s.Datei.Anlegen(pw, daten));
            }
            else
            {
                if (_fehlversuche >= 3) await Task.Delay(Math.Min(10, _fehlversuche) * 500);
                daten = await Task.Run(() => _s.Datei.Oeffnen(pw));
            }
            _fehlversuche = 0;
            Entsperrt?.Invoke(daten);
        }
        catch (FalschesPasswortException)
        {
            _fehlversuche++;
            Fehler("Das Passwort ist falsch.");
            _pw.Box.SelectAll();
        }
        catch (Exception e) when (e is TresorDefektException or IOException or UnauthorizedAccessException)
        {
            Fehler(e.Message + (e is TresorDefektException ? $"\nSicherungen liegen in {_s.Datei.SicherungsOrdner}" : ""));
        }
        finally
        {
            _busy = false;
            if (!IsDisposed)
            {
                _ok.Enabled = true;
                _ok.Text = _neu ? "Tresor anlegen" : "Entsperren";
            }
        }
    }

    private void Fehler(string text)
    {
        _fehler.ForeColor = Theme.P.Danger;
        _fehler.Text = text;
        _pw.Box.Focus();
    }
}
