using BankManager.Banking;
using BankManager.Core;

namespace BankManager.Gui;

/// <summary>Fetches balances via FinTS for the given banks; asks for PIN/TAN or app approval when the bank wants it.</summary>
public sealed class AbrufDialog : DialogBase
{
    private readonly Sitzung _s;
    private readonly List<Bank> _banken;
    private readonly Label _status;
    private readonly Label _protokoll;
    private readonly InputBox _tan = new();
    private readonly FlatButton _tanOk = new("TAN senden", ButtonKind.Primary);
    private readonly CancellationTokenSource _cts = new();
    private TaskCompletionSource<string?>? _warteAufTan;

    public int Aktualisiert { get; private set; }

    public AbrufDialog(Sitzung s, List<Bank> banken) : base("Kontostände abrufen", "Schließen", 560, 460)
    {
        _s = s;
        _banken = banken;
        AddHeading("Kontostände direkt von deiner Bank abrufen");
        AddHint("Es werden nur Kontostände gelesen – Überweisungen sind mit dem Bank-Manager nicht möglich.");
        Y += Ui.S(6);
        _status = Make.Label("Verbinde …", Ui.Bold, width: ContentWidth);
        _status.Tag = "keepcolor";
        Place(_status, Ui.S(44), 6);
        _tan.Width = ContentWidth - _tanOk.Width - Ui.S(8);
        AddRow(_tan, _tanOk);
        _tan.Visible = _tanOk.Visible = false;
        _protokoll = Make.Label("", Ui.Base, width: ContentWidth);
        Place(_protokoll, Ui.S(200), 6);
        FinishLayout();
        OkButton.Enabled = false;
        OkButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        CancelBtn.Click += (_, _) => _cts.Cancel();
        _tanOk.Click += (_, _) => TanFertig(_tan.Text.Trim());
        _tan.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; TanFertig(_tan.Text.Trim()); } };
    }

    private void TanFertig(string? tan)
    {
        _tan.Visible = _tanOk.Visible = false;
        _warteAufTan?.TrySetResult(tan);
    }

    private void Zeile(string text)
    {
        _protokoll.Text = (_protokoll.Text.Length > 0 ? _protokoll.Text + "\n" : "") + text;
    }

    private void Status(string text, Color? farbe = null)
    {
        _status.ForeColor = farbe ?? Theme.P.Text;
        _status.Text = text;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var abruf = new KontoAbruf(_s.Einstellungen.FinTsProduktnummer, Updater.CurrentVersion.ToString(3));
        foreach (var bank in _banken)
        {
            if (_cts.IsCancellationRequested || IsDisposed) break;
            var z = bank.FinTs!;
            string pin = z.Pin;
            if (!z.PinMerken || pin.Length == 0)
            {
                using var frage = new PasswortAbfrage($"PIN für {bank.Name}", $"Gib deine Online-Banking-PIN für {bank.Name} ein. Sie wird nur für diesen Abruf verwendet und nicht gespeichert.");
                if (frage.ShowDialog(this) != DialogResult.OK) { Zeile($"{bank.Name}: übersprungen"); continue; }
                pin = frage.Passwort;
            }
            var konten = bank.Konten.Where(k => KontoAbruf.KontonummerAus(k.Iban) != null).ToList();
            if (konten.Count == 0) { Zeile($"{bank.Name}: kein Konto mit deutscher IBAN eingetragen"); continue; }
            bool fehler = false;
            foreach (var konto in konten)
            {
                if (_cts.IsCancellationRequested || IsDisposed) break;
                Status($"{bank.Name} – {konto.Name}: verbinde …");
                var r = await abruf.SaldoAbrufen(bank, konto, pin, TanAnfrage, _cts.Token);
                if (IsDisposed) return;
                if (r.Ok && r.Kontostand is decimal stand)
                {
                    konto.StandSetzen(stand);
                    Aktualisiert++;
                    Zeile($"✓ {bank.Name} – {konto.Name}: {_s.Geld(stand, konto.Waehrung)}");
                }
                else
                {
                    fehler = true;
                    z.LetzterFehler = r.Meldung;
                    Zeile($"✗ {bank.Name} – {konto.Name}: {r.Meldung}");
                }
            }
            if (!fehler) { z.LetzterAbruf = DateTime.Now; z.LetzterFehler = ""; }
        }
        if (IsDisposed) return;
        if (_s.Daten != null) _s.Speichern(this);
        Status(Aktualisiert > 0 ? $"Fertig – {Wort.Anzahl(Aktualisiert, "Kontostand", "Kontostände")} aktualisiert." : "Fertig – nichts aktualisiert.",
            Aktualisiert > 0 ? Theme.P.Success : Theme.P.Warning);
        OkButton.Enabled = true;
        CancelBtn.Visible = false;
        AcceptButton = OkButton;
    }

    private Task<string?> TanAnfrage(Freigabe f)
    {
        var tcs = new TaskCompletionSource<string?>();
        BeginInvoke(() =>
        {
            if (f.InAppFreigeben)
            {
                Status("Bitte gib den Abruf jetzt in deiner Banking-App frei …" + (f.Hinweis.Length > 0 ? "\n" + f.Hinweis : ""), Theme.P.Accent);
                tcs.TrySetResult("");
                return;
            }
            Status("Deine Bank möchte eine TAN:" + (f.Hinweis.Length > 0 ? "\n" + f.Hinweis : ""), Theme.P.Accent);
            _tan.Text = "";
            _tan.Visible = _tanOk.Visible = true;
            _tan.Box.Focus();
            _warteAufTan = tcs;
        });
        _cts.Token.Register(() => tcs.TrySetResult(null));
        return tcs.Task;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts.Cancel();
        _warteAufTan?.TrySetResult(null);
        base.OnFormClosing(e);
    }
}
