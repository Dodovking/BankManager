using System.Diagnostics;
using System.Runtime.InteropServices;
using BankManager.Core;
using Microsoft.Win32;

namespace BankManager.Gui;

public sealed class MainForm : Form
{
    private readonly Sitzung _s;
    private readonly UpdateChecker _updateChecker;
    private readonly Panel _banner = new() { Dock = DockStyle.Top, Visible = false };
    private readonly Label _bannerText = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, Font = Ui.Bold, Tag = "keepcolor" };
    private readonly FlatButton _bannerJetzt = new("Jetzt aktualisieren", ButtonKind.Primary);
    private readonly FlatButton _bannerSpaeter = new("Später", ButtonKind.Ghost);
    private readonly Panel _root = new() { Dock = DockStyle.Fill };
    private readonly Panel _shell = new() { Dock = DockStyle.Fill };
    private readonly Panel _sidebar = new() { Dock = DockStyle.Left, Tag = "surface" };
    private readonly Panel _header = new() { Dock = DockStyle.Top };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Panel _status = new() { Dock = DockStyle.Bottom };
    private readonly Label _statusText = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, Font = Ui.Small, Tag = "muted" };
    private readonly Label _titel = Make.Label("", Ui.Title);
    private readonly Label _untertitel = Make.Label("", Ui.Small, muted: true);
    private readonly FlatButton _aktion = new("Aktion", ButtonKind.Primary);
    private readonly FlatButton _updateBtn = new("Updates", ButtonKind.Ghost);
    private readonly SymbolButton _verbergen = new(Glyph.Auge, "Beträge verbergen", 36);
    private readonly SymbolButton _sperren = new(Glyph.Schloss, "Jetzt sperren", 36);
    private readonly List<(NavButton Button, Seite Seite)> _nav = [];
    private readonly System.Windows.Forms.Timer _sperrTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer _statusTimer = new();
    private SperrSeite? _sperrSeite;
    private Seite? _aktuell;
    private UpdateInfo? _verfuegbar;
    private bool _updating;
    private bool _gefragt;

    public UebersichtSeite Uebersicht { get; }
    public BankenSeite Banken { get; }
    public BankDetailSeite BankDetail { get; }
    public KontenSeite Konten { get; }
    public KartenSeite Karten { get; }
    public ZahlungenSeite Zahlungen { get; }
    public EinstellungenSeite EinstellungenSeite { get; }

    public string[] StartArgs { get; init; } = [];
    public bool NachUpdate { get; init; }
    public bool Fotomodus { get; init; }
    public Sitzung S => _s;

    public MainForm(Sitzung s, string? updateSource)
    {
        _s = s;
        _updateChecker = new UpdateChecker(releaseUrl: updateSource);
        Text = "Bank-Manager";
        Icon = Ui.AppIcon();
        Font = Ui.Base;
        AutoScaleMode = AutoScaleMode.None;
        MinimumSize = new Size(Ui.S(980), Ui.S(640));
        StartPosition = FormStartPosition.CenterScreen;
        var e = s.Einstellungen;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        Size = e.FensterBreite > 0
            ? new Size(Math.Min(e.FensterBreite, area.Width), Math.Min(e.FensterHoehe, area.Height))
            : new Size(Math.Min(Ui.S(1280), area.Width), Math.Min(Ui.S(820), area.Height));
        if (e.Maximiert) WindowState = FormWindowState.Maximized;
        KeyPreview = true;

        Uebersicht = new UebersichtSeite(this);
        Banken = new BankenSeite(this);
        BankDetail = new BankDetailSeite(this);
        Konten = new KontenSeite(this);
        Karten = new KartenSeite(this);
        Zahlungen = new ZahlungenSeite(this);
        EinstellungenSeite = new EinstellungenSeite(this);

        BaueBanner();
        BaueSidebar();
        BaueHeader();
        _status.Height = Ui.S(30);
        _status.Padding = new Padding(Ui.S(20), 0, Ui.S(20), 0);
        _status.Controls.Add(_statusText);
        _content.Padding = new Padding(Ui.S(28), Ui.S(4), Ui.S(28), Ui.S(16));
        _shell.Controls.Add(_content);
        _shell.Controls.Add(_status);
        _shell.Controls.Add(_header);
        _shell.Controls.Add(_sidebar);
        Controls.Add(_root);
        Controls.Add(_banner);

        _sperrTimer.Tick += (_, _) => AutoSperrePruefen();
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusStandard(); };
        SystemEvents.SessionSwitch += SessionSwitch;
        Theme.Changed += ThemeGeaendert;
        _s.Geaendert += () => { if (_s.Daten != null) _aktuell?.Aktualisieren(); StatusStandard(); };
        Sperren();
    }

    private void BaueBanner()
    {
        _banner.Height = Ui.S(52);
        _banner.Padding = new Padding(Ui.S(20), 0, Ui.S(16), 0);
        _banner.Controls.AddRange([_bannerText, _bannerJetzt, _bannerSpaeter]);
        _banner.Layout += (_, _) =>
        {
            int y = (_banner.Height - _bannerJetzt.Height) / 2;
            _bannerSpaeter.Location = new Point(_banner.Width - Ui.S(16) - _bannerSpaeter.Width, y);
            _bannerJetzt.Location = new Point(_bannerSpaeter.Left - Ui.S(8) - _bannerJetzt.Width, y);
            _bannerText.Bounds = new Rectangle(Ui.S(52), 0, Math.Max(10, _bannerJetzt.Left - Ui.S(64)), _banner.Height);
        };
        _banner.Paint += (_, e) =>
        {
            TextRenderer.DrawText(e.Graphics, Glyph.Update, Glyph.Gross, new Rectangle(Ui.S(14), 0, Ui.S(32), _banner.Height), Theme.P.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            using var pen = new Pen(Theme.P.Border);
            e.Graphics.DrawLine(pen, 0, _banner.Height - 1, _banner.Width, _banner.Height - 1);
        };
        _bannerJetzt.Click += (_, _) => UpdateOeffnen();
        _bannerSpaeter.Click += (_, _) => _banner.Visible = false;
    }

    private void BaueSidebar()
    {
        _sidebar.Width = Ui.S(232);
        var marke = new Panel { Dock = DockStyle.Top, Height = Ui.S(84) };
        marke.Paint += (_, e) =>
        {
            var g = e.Graphics;
            using (var ico = new Icon(Ui.AppIcon(), new Size(Ui.S(36), Ui.S(36))))
                g.DrawIcon(ico, new Rectangle(Ui.S(24), Ui.S(26), Ui.S(36), Ui.S(36)));
            TextRenderer.DrawText(g, "Bank-Manager", Ui.CardTitle, new Point(Ui.S(70), Ui.S(24)), Theme.P.Text, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "Deine Banken im Blick", Ui.Small, new Point(Ui.S(71), Ui.S(46)), Theme.P.Muted, TextFormatFlags.NoPrefix);
        };
        var liste = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, Ui.S(8), 0, 0) };
        var eintraege = new (string Symbol, string Text, Seite Seite)[]
        {
            (Glyph.Home, "Übersicht", Uebersicht),
            (Glyph.Bank, "Banken", Banken),
            (Glyph.Liste, "Konten", Konten),
            (Glyph.Karte, "Karten", Karten),
            (Glyph.Wiederholen, "Regelmäßige Zahlungen", Zahlungen),
            (Glyph.Einstellungen, "Einstellungen", EinstellungenSeite),
        };
        int y = Ui.S(8);
        foreach (var (sym, text, seite) in eintraege)
        {
            var b = new NavButton(sym, text) { Location = new Point(0, y), Width = _sidebar.Width };
            b.Click += (_, _) => Zeige(seite);
            liste.Controls.Add(b);
            _nav.Add((b, seite));
            y += b.Height + Ui.S(4);
        }
        var fuss = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(60) };
        var sperrNav = new NavButton(Glyph.Schloss, "Sperren") { Location = new Point(0, Ui.S(8)), Width = _sidebar.Width };
        sperrNav.Click += (_, _) => Sperren();
        fuss.Controls.Add(sperrNav);
        _sidebar.Controls.Add(liste);
        _sidebar.Controls.Add(fuss);
        _sidebar.Controls.Add(marke);
        _sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.P.Border);
            e.Graphics.DrawLine(pen, _sidebar.Width - 1, 0, _sidebar.Width - 1, _sidebar.Height);
        };
    }

    private void BaueHeader()
    {
        _header.Height = Ui.S(88);
        _header.Controls.AddRange([_titel, _untertitel, _aktion, _updateBtn, _verbergen, _sperren]);
        _header.Layout += (_, _) =>
        {
            _titel.Location = new Point(Ui.S(28), Ui.S(20));
            _untertitel.Location = new Point(Ui.S(30), Ui.S(54));
            int y = Ui.S(26);
            int x = _header.Width - Ui.S(28);
            if (_aktion.Visible) { x -= _aktion.Width; _aktion.Location = new Point(x, y); x -= Ui.S(10); }
            x -= _sperren.Width; _sperren.Location = new Point(x, y - 1); x -= Ui.S(4);
            x -= _verbergen.Width; _verbergen.Location = new Point(x, y - 1); x -= Ui.S(8);
            x -= _updateBtn.Width; _updateBtn.Location = new Point(x, y);
        };
        _aktion.Click += (_, _) => _aktuell?.HauptAktion(_aktion);
        _updateBtn.Click += (_, _) => UpdateOeffnen();
        _verbergen.Click += (_, _) => VerbergenUmschalten();
        _sperren.Click += (_, _) => Sperren();
    }

    // ---------- Navigation ----------

    public void Zeige(Seite seite)
    {
        if (_s.Daten == null) return;
        if (_aktuell != seite)
        {
            _content.SuspendLayout();
            _content.Controls.Clear();
            seite.Dock = DockStyle.Fill;
            _content.Controls.Add(seite);
            _content.ResumeLayout();
            _aktuell = seite;
            Theme.Apply(seite);
        }
        foreach (var (b, sp) in _nav) b.Aktiv = sp == seite || (seite == BankDetail && sp == Banken);
        seite.Aktualisieren();
        KopfAktualisieren();
    }

    public void KopfAktualisieren()
    {
        if (_aktuell == null) return;
        _titel.Text = _aktuell.Titel;
        _untertitel.Text = _aktuell.Untertitel;
        var text = _aktuell.AktionText;
        _aktion.Visible = text != null;
        if (text != null) { _aktion.Text = text; _aktion.FitWidth(Ui.S(120)); }
        _header.PerformLayout();
        _header.Invalidate(true);
    }

    public void BankOeffnen(Bank bank)
    {
        BankDetail.Bank = bank;
        Zeige(BankDetail);
    }

    public Seite? Aktuell => _aktuell;

    // ---------- Sperren / Entsperren ----------

    public void Sperren()
    {
        foreach (var f in Application.OpenForms.Cast<Form>().Where(f => f != this).ToList())
        {
            try { f.DialogResult = DialogResult.Cancel; f.Close(); } catch { }
        }
        _sperrTimer.Stop();
        _s.Sperren();
        _aktuell = null;
        _content.Controls.Clear();
        _root.Controls.Clear();
        _sperrSeite?.Dispose();
        _sperrSeite = new SperrSeite(_s);
        _sperrSeite.Entsperrt += Entsperren;
        _sperrSeite.Zurueckgesetzt += () => BeginInvoke(Sperren);
        _root.Controls.Add(_sperrSeite);
        Theme.Apply(_sperrSeite);
        if (IsHandleCreated) BeginInvoke(() => _sperrSeite?.Fokus());
    }

    public void Entsperren(Tresor daten)
    {
        _root.Controls.Clear();
        _sperrSeite?.Dispose();
        _sperrSeite = null;
        _root.Controls.Add(_shell);
        Theme.Apply(_shell);
        _s.Entsperrt(daten);
        BankDetail.Bank = null;
        Zeige(Uebersicht);
        VerbergenSymbol();
        if (!Fotomodus) _sperrTimer.Start();
        StatusStandard();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private void AutoSperrePruefen()
    {
        int min = _s.Einstellungen.AutoSperreMinuten;
        if (_s.Daten == null || min <= 0) return;
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return;
        uint idle = unchecked((uint)Environment.TickCount - info.dwTime);
        if (idle >= (uint)min * 60_000u) Sperren();
    }

    private void SessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            try { BeginInvoke(() => { if (_s.Daten != null) Sperren(); }); } catch { }
    }

    // ---------- Gemeinsame Aktionen ----------

    private void VerbergenUmschalten()
    {
        _s.Einstellungen.BetraegeVerbergen = !_s.Einstellungen.BetraegeVerbergen;
        _s.EinstellungenSpeichern();
        VerbergenSymbol();
        _aktuell?.Aktualisieren();
    }

    private void VerbergenSymbol()
    {
        bool v = _s.Einstellungen.BetraegeVerbergen;
        _verbergen.Symbol = v ? Glyph.AugeZu : Glyph.Auge;
        _verbergen.SetTooltip(v ? "Beträge anzeigen" : "Beträge verbergen");
    }

    public void Status(string text, bool warnung = false)
    {
        _statusText.Tag = "keepcolor";
        _statusText.ForeColor = warnung ? Theme.P.Warning : Theme.P.Success;
        _statusText.Text = text;
        _statusTimer.Stop();
        _statusTimer.Interval = 6000;
        _statusTimer.Start();
    }

    private void StatusStandard()
    {
        _statusText.Tag = "muted";
        _statusText.ForeColor = Theme.P.Muted;
        if (_s.Daten == null) { _statusText.Text = ""; return; }
        var t = _s.Daten;
        int karten = t.AlleKarten.Count();
        var sperre = _s.Einstellungen.AutoSperreMinuten > 0 ? $"sperrt nach {_s.Einstellungen.AutoSperreMinuten} Min. ohne Eingabe" : "automatische Sperre aus";
        _statusText.Text = $"{t.Banken.Count} Banken  ·  {t.AlleKonten.Count()} Konten  ·  {karten} Karten  ·  verschlüsselt gespeichert  ·  {sperre}  ·  Version {Updater.CurrentVersion.ToString(3)}";
    }

    public void Kopieren(string text, string was, bool geheim = false)
    {
        if (Zwischenablage.Kopieren(text, geheim))
            Status(geheim ? $"{was} kopiert – wird in 30 Sekunden automatisch aus der Zwischenablage gelöscht." : $"{was} kopiert.");
    }

    public void OnlineBanking(Bank bank)
    {
        if (string.IsNullOrWhiteSpace(bank.Website))
        {
            MessageBox.Show(this, $"Für {bank.Name} ist noch keine Online-Banking-Adresse eingetragen.\nDu kannst sie unter „Bearbeiten“ eintragen.", "Online-Banking", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Uri.TryCreate(bank.Website, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            MessageBox.Show(this, "Die Adresse ist ungültig: " + bank.Website, "Online-Banking", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Online-Banking", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private bool Bestaetigen(string frage, string titel) =>
        MessageBox.Show(this, frage, titel, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    public void BankNeu()
    {
        var bank = new Bank();
        using var d = new BankDialog(bank, neu: true);
        if (d.ShowDialog(this) != DialogResult.OK || _s.Daten == null) return;
        _s.T.Banken.Add(bank);
        if (_s.Speichern(this)) Status($"{bank.Name} hinzugefügt.");
    }

    public void BankBearbeiten(Bank bank)
    {
        using var d = new BankDialog(bank, neu: false);
        if (d.ShowDialog(this) != DialogResult.OK || _s.Daten == null) return;
        if (_s.Speichern(this)) Status($"{bank.Name} gespeichert.");
    }

    public void BankLoeschen(Bank bank)
    {
        var extra = bank.Konten.Count > 0 ? $"\n\nDabei werden auch {bank.Konten.Count} Konto/Konten mit Karten und Verlauf gelöscht." : "";
        if (!Bestaetigen($"{bank.Name} wirklich löschen?{extra}", "Bank löschen")) return;
        var ids = bank.Konten.Select(k => k.Id).ToHashSet();
        _s.T.Banken.Remove(bank);
        foreach (var z in _s.T.Zahlungen.Where(z => z.KontoId != null && ids.Contains(z.KontoId))) z.KontoId = null;
        if (_aktuell == BankDetail) { BankDetail.Bank = null; Zeige(Banken); }
        if (_s.Speichern(this)) Status($"{bank.Name} gelöscht.");
    }

    public void BankVerschieben(Bank bank, int richtung)
    {
        var l = _s.T.Banken;
        int i = l.IndexOf(bank), j = i + richtung;
        if (i < 0 || j < 0 || j >= l.Count) return;
        (l[i], l[j]) = (l[j], l[i]);
        _s.Speichern(this);
    }

    public void KontoNeu(Bank? bank)
    {
        if (_s.T.Banken.Count == 0) { BankNeu(); return; }
        var konto = new Konto();
        using var d = new KontoDialog(_s.T, konto, bank, neu: true);
        if (d.ShowDialog(this) != DialogResult.OK || _s.Daten == null) return;
        var ziel = bank ?? d.GewaehlteBank;
        if (ziel == null) return;
        ziel.Konten.Add(konto);
        if (_s.Speichern(this)) Status($"{konto.Name} bei {ziel.Name} angelegt.");
    }

    public void KontoBearbeiten(Konto konto)
    {
        var bank = _s.T.BankVon(konto);
        using var d = new KontoDialog(_s.T, konto, bank, neu: false);
        if (d.ShowDialog(this) != DialogResult.OK || _s.Daten == null) return;
        if (_s.Speichern(this)) Status($"{konto.Name} gespeichert.");
    }

    public void KontoLoeschen(Konto konto)
    {
        var bank = _s.T.BankVon(konto);
        if (bank == null) return;
        var extra = konto.Karten.Count > 0 ? $" Die {konto.Karten.Count} Karte(n) dazu werden ebenfalls gelöscht." : "";
        if (!Bestaetigen($"Konto „{konto.Name}“ bei {bank.Name} wirklich löschen?{extra}", "Konto löschen")) return;
        bank.Konten.Remove(konto);
        foreach (var z in _s.T.Zahlungen.Where(z => z.KontoId == konto.Id)) z.KontoId = null;
        if (_s.Speichern(this)) Status($"{konto.Name} gelöscht.");
    }

    public void StandEintragen(Konto konto)
    {
        var bank = _s.T.BankVon(konto);
        if (bank == null) return;
        using var d = new StandDialog(_s, bank, konto);
        if (d.ShowDialog(this) != DialogResult.OK || _s.Daten == null) return;
        if (_s.Speichern(this)) Status($"Kontostand für {konto.Name} ({bank.Name}) eingetragen.");
    }

    public void StandMenue(Control anker)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        foreach (var b in _s.T.Banken)
        foreach (var k in b.Konten)
        {
            var konto = k;
            menu.Items.Add(new ToolStripMenuItem($"{b.Name} – {k.Name}   ({_s.Geld(k.Kontostand, k.Waehrung)})", null, (_, _) => StandEintragen(konto)) { Font = Ui.Base });
        }
        if (menu.Items.Count == 0)
        {
            menu.Dispose();
            if (MessageBox.Show(this, "Du hast noch keine Konten. Jetzt ein Konto anlegen?", "Kontostand", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) KontoNeu(null);
            return;
        }
        Optik.StyleMenu(menu);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anker, new Point(anker.Width - menu.PreferredSize.Width, anker.Height + Ui.S(4)));
    }

    public void KarteNeu(Konto? konto)
    {
        if (!_s.T.AlleKonten.Any())
        {
            if (MessageBox.Show(this, "Eine Karte gehört immer zu einem Konto. Jetzt zuerst ein Konto anlegen?", "Karte hinzufügen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) KontoNeu(null);
            return;
        }
        var karte = new Karte();
        using var d = new KarteDialog(_s.T, karte, konto, neu: true);
        if (d.ShowDialog(this) != DialogResult.OK || _s.Daten == null || d.GewaehltesKonto == null) return;
        d.GewaehltesKonto.Karten.Add(karte);
        if (_s.Speichern(this)) Status($"{karte.Bezeichnung} hinzugefügt.");
    }

    public void KarteBearbeiten(Konto konto, Karte karte)
    {
        using var d = new KarteDialog(_s.T, karte, konto, neu: false);
        if (d.ShowDialog(this) != DialogResult.OK || _s.Daten == null) return;
        if (d.GewaehltesKonto != null && d.GewaehltesKonto != konto)
        {
            konto.Karten.Remove(karte);
            d.GewaehltesKonto.Karten.Add(karte);
        }
        if (_s.Speichern(this)) Status($"{karte.Bezeichnung} gespeichert.");
    }

    public void KarteLoeschen(Konto konto, Karte karte)
    {
        if (!Bestaetigen($"Karte „{karte.Bezeichnung}“ wirklich löschen?", "Karte löschen")) return;
        konto.Karten.Remove(karte);
        if (_s.Speichern(this)) Status($"{karte.Bezeichnung} gelöscht.");
    }

    public void ZahlungNeu()
    {
        var z = new Zahlung();
        using var d = new ZahlungDialog(_s.T, z, neu: true);
        if (d.ShowDialog(this) != DialogResult.OK || _s.Daten == null) return;
        _s.T.Zahlungen.Add(z);
        if (_s.Speichern(this)) Status($"{z.Name} hinzugefügt.");
    }

    public void ZahlungBearbeiten(Zahlung z)
    {
        using var d = new ZahlungDialog(_s.T, z, neu: false);
        if (d.ShowDialog(this) != DialogResult.OK || _s.Daten == null) return;
        if (_s.Speichern(this)) Status($"{z.Name} gespeichert.");
    }

    public void ZahlungLoeschen(Zahlung z)
    {
        if (!Bestaetigen($"„{z.Name}“ wirklich löschen?", "Zahlung löschen")) return;
        _s.T.Zahlungen.Remove(z);
        if (_s.Speichern(this)) Status($"{z.Name} gelöscht.");
    }

    // ---------- Updates ----------

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.ApplyTitleBar(this);
        _sperrSeite?.Fokus();
        if (NachUpdate)
            ZeigeBanner($"Update installiert – du nutzt jetzt Version {Updater.CurrentVersion.ToString(3)}.", nurInfo: true);
        if (!Fotomodus && _s.Einstellungen.UpdatesPruefen) await UpdatesStillPruefen();
    }

    public async Task UpdatesStillPruefen()
    {
        UpdateInfo? info;
        try { info = await _updateChecker.GetLatestAsync(); }
        catch (Exception ex) when (ex is UpdateException or OperationCanceledException) { return; }
        if (IsDisposed) return;
        VerfuegbarSetzen(info);
        if (_verfuegbar != null && !_gefragt && !_updating)
        {
            _gefragt = true;
            var frage = $"Es gibt ein Update für den Bank-Manager!\n\nInstalliert: Version {Updater.CurrentVersion.ToString(3)}\nNeu: Version {_verfuegbar.Version.ToString(3)}\n\nJetzt aktualisieren? Deine Daten bleiben dabei erhalten.";
            if (MessageBox.Show(this, frage, "Update verfügbar", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                UpdateOeffnen();
        }
    }

    private void VerfuegbarSetzen(UpdateInfo? info)
    {
        _verfuegbar = info != null && info.IsNewerThan(Updater.CurrentVersion) ? info : null;
        _updateBtn.Text = _verfuegbar == null ? "Updates" : $"Update {_verfuegbar.Version.ToString(3)}";
        _updateBtn.Kind = _verfuegbar == null ? ButtonKind.Ghost : ButtonKind.Primary;
        _updateBtn.FitWidth();
        _updateBtn.Invalidate();
        _header.PerformLayout();
        if (_verfuegbar != null)
            ZeigeBanner($"Neue Version {_verfuegbar.Version.ToString(3)} ist verfügbar  ·  Installiert: {Updater.CurrentVersion.ToString(3)}", nurInfo: false);
        EinstellungenSeite.UpdateStatus(_verfuegbar);
    }

    private void ZeigeBanner(string text, bool nurInfo)
    {
        _bannerText.Text = text;
        _bannerText.ForeColor = Theme.P.Text;
        _bannerJetzt.Visible = !nurInfo;
        _bannerSpaeter.Text = nurInfo ? "OK" : "Später";
        _bannerSpaeter.FitWidth();
        _banner.BackColor = Ui.Blend(Theme.P.Back, nurInfo ? Theme.P.Success : Theme.P.Accent, 0.16f);
        _banner.Visible = true;
        _banner.PerformLayout();
    }

    public void UpdateOeffnen()
    {
        if (_updating) return;
        using var f = new UpdateForm(_updateChecker, _verfuegbar);
        var result = f.ShowDialog(this);
        VerfuegbarSetzen(f.Info);
        if (result == DialogResult.OK && f.Info != null) UpdateAusfuehren();
    }

    private void UpdateAusfuehren()
    {
        _updating = true;
        try
        {
            if (_s.Daten != null) _s.Datei.Speichern(_s.Daten);
            Updater.Swap();
            Updater.StartUpdated(StartArgs);
        }
        catch (Exception e)
        {
            try { Updater.Rollback(); } catch { }
            _updating = false;
            MessageBox.Show(this, "Das Update konnte nicht installiert werden:\n\n" + e.Message + "\n\nDie bisherige Version läuft unverändert weiter.",
                "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Close();
    }

    // ---------- Fenster ----------

    public void Zeigen()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeGeaendert();
    }

    private void ThemeGeaendert()
    {
        Theme.Apply(this);
        if (_aktuell != null && _aktuell.Parent == null) Theme.Apply(_aktuell);
        _sidebar.BackColor = Theme.P.Surface;
        _header.BackColor = Theme.P.Back;
        _status.BackColor = Theme.P.Back;
        if (_banner.Visible) _banner.BackColor = Ui.Blend(Theme.P.Back, _bannerJetzt.Visible ? Theme.P.Accent : Theme.P.Success, 0.16f);
        _bannerText.ForeColor = Theme.P.Text;
        _titel.ForeColor = Theme.P.Text;
        _untertitel.ForeColor = Theme.P.Muted;
        StatusStandard();
        _aktuell?.Aktualisieren();
        Invalidate(true);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.L)) { Sperren(); return true; }
        if (_s.Daten != null && keyData is >= (Keys.Control | Keys.D1) and <= (Keys.Control | Keys.D6))
        {
            Zeige(_nav[(int)(keyData & ~Keys.Control) - (int)Keys.D1].Seite);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var st = _s.Einstellungen;
        st.Maximiert = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        st.FensterBreite = b.Width;
        st.FensterHoehe = b.Height;
        if (!Fotomodus) _s.EinstellungenSpeichern();
        SystemEvents.SessionSwitch -= SessionSwitch;
        Theme.Changed -= ThemeGeaendert;
        _sperrTimer.Stop();
        _s.Sperren();
        base.OnFormClosing(e);
    }
}
