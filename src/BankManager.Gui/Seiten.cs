using BankManager.Core;

namespace BankManager.Gui;

public abstract class Seite : Panel
{
    protected readonly MainForm App;
    protected Sitzung S => App.S;
    protected Tresor T => App.S.T;

    protected Seite(MainForm app)
    {
        App = app;
        DoubleBuffered = true;
    }

    public abstract string Titel { get; }
    public virtual string Untertitel => "";
    public virtual string? AktionText => null;
    public virtual void HauptAktion(Control anker) { }
    public abstract void Aktualisieren();
}

/// <summary>Card with a heading drawn at the top; child controls start below <see cref="Oben"/>.</summary>
public class TitelKarte : Card
{
    public string Titel { get; set; } = "";
    public string Rechts { get; set; } = "";
    public static int Oben => Ui.S(52);

    protected override void PaintContent(Graphics g)
    {
        TextRenderer.DrawText(g, Titel, Ui.Section, new Point(Ui.S(20), Ui.S(18)), Theme.P.Text, TextFormatFlags.NoPrefix);
        if (Rechts.Length > 0)
            TextRenderer.DrawText(g, Rechts, Ui.Small, new Rectangle(0, Ui.S(20), Width - Ui.S(20), Ui.S(20)), Theme.P.Muted, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
    }
}

// ============================== Übersicht ==============================

public sealed class UebersichtSeite : Seite
{
    private readonly KennzahlKarte _vermoegen = new() { Titel = "Guthaben gesamt", Symbol = Glyph.Geld };
    private readonly KennzahlKarte _schulden = new() { Titel = "Schulden & Kredite", Symbol = Glyph.Warnung };
    private readonly KennzahlKarte _netto = new() { Titel = "Nettovermögen", Symbol = Glyph.Bank };
    private readonly KennzahlKarte _fix = new() { Titel = "Fixkosten pro Monat", Symbol = Glyph.Wiederholen };
    private readonly TitelKarte _verteilung = new() { Titel = "Verteilung nach Bank" };
    private readonly TitelKarte _verlauf = new() { Titel = "Verlauf Nettovermögen" };
    private readonly TitelKarte _hinweise = new() { Titel = "Hinweise" };
    private readonly TitelKarte _banken = new() { Titel = "Deine Banken" };
    private readonly DonutDiagramm _donut = new();
    private readonly LinienDiagramm _linie = new();
    private readonly Segmented _zeitraum = new("3 Monate", "1 Jahr", "Alles") { SelectedIndex = 1 };
    private readonly Panel _innen = new();

    public UebersichtSeite(MainForm app) : base(app)
    {
        AutoScroll = true;
        _verteilung.Controls.Add(_donut);
        _verlauf.Controls.Add(_linie);
        _verlauf.Controls.Add(_zeitraum);
        _zeitraum.SelectedIndexChanged += (_, _) => VerlaufFuellen();
        _innen.Controls.AddRange([_vermoegen, _schulden, _netto, _fix, _verteilung, _verlauf, _banken, _hinweise]);
        Controls.Add(_innen);
        Resize += (_, _) => Anordnen();
    }

    public override string Titel => "Übersicht";
    public override string Untertitel => $"Stand {DateTime.Now:dddd, d. MMMM yyyy}";
    public override string? AktionText => "Kontostand eintragen";
    public override void HauptAktion(Control anker) => App.StandMenue(anker);

    public override void Aktualisieren()
    {
        if (S.Daten == null) return;
        var t = T;
        decimal verm = Finanzen.Vermoegen(t), schuld = Finanzen.Schulden(t), netto = Finanzen.Netto(t);
        decimal aus = Finanzen.AusgabenProMonat(t), ein = Finanzen.EinnahmenProMonat(t);
        int konten = t.AlleKonten.Count();
        _vermoegen.Wert = S.Geld(verm);
        _vermoegen.Unterzeile = $"auf {t.AlleKonten.Count(k => k.Kontostand > 0)} von {konten} Konten";
        _vermoegen.SymbolFarbe = Theme.P.Success;
        _schulden.Wert = S.Geld(schuld);
        _schulden.WertFarbe = schuld > 0 ? Theme.P.Danger : null;
        _schulden.SymbolFarbe = schuld > 0 ? Theme.P.Danger : Theme.P.Muted;
        _schulden.Unterzeile = schuld > 0 ? $"{Wort.Anzahl(t.AlleKonten.Count(k => k.Kontostand < 0), "Konto", "Konten")} im Minus" : "keine Schulden eingetragen";
        _netto.Wert = S.Geld(netto);
        _netto.WertFarbe = netto < 0 ? Theme.P.Danger : null;
        _netto.Unterzeile = "Guthaben minus Schulden";
        _fix.Wert = S.Geld(aus);
        _fix.SymbolFarbe = Theme.P.Warning;
        _fix.Unterzeile = ein > 0 ? $"Einnahmen {S.Geld(ein)} · übrig {S.Geld(ein - aus)}" : Wort.Anzahl(t.Zahlungen.Count(z => z.Aktiv && !z.IstEinnahme), "regelmäßige Ausgabe", "regelmäßige Ausgaben");

        _donut.Teile.Clear();
        foreach (var (bank, summe) in Finanzen.ProBank(t).Where(x => x.Summe != 0))
            _donut.Teile.Add((bank.Name, summe, Farben.Lesen(bank.Farbe, Theme.P.Accent)));
        _donut.Format = d => S.Geld(d);
        _linie.Format = d => S.Geld(d);
        _linie.Verborgen = S.Einstellungen.BetraegeVerbergen;
        VerlaufFuellen();
        BankenFuellen();
        HinweiseFuellen();
        Anordnen();
        Invalidate(true);
    }

    private void VerlaufFuellen()
    {
        if (S.Daten == null) return;
        DateTime? ab = _zeitraum.SelectedIndex switch { 0 => DateTime.Today.AddMonths(-3), 1 => DateTime.Today.AddYears(-1), _ => null };
        _linie.Punkte.Clear();
        _linie.Punkte.AddRange(Finanzen.Verlauf(T, ab));
        if (_linie.Punkte.Count > 0 && _linie.Punkte[^1].Datum.Date < DateTime.Today)
            _linie.Punkte.Add((DateTime.Today, _linie.Punkte[^1].Wert));
        _linie.Invalidate();
    }

    private void BankenFuellen()
    {
        foreach (var c in _banken.Controls.Cast<Control>().ToList()) { _banken.Controls.Remove(c); c.Dispose(); }
        foreach (var b in T.Banken)
        {
            var zeile = new BankZeile(App, b);
            _banken.Controls.Add(zeile);
        }
        if (T.Banken.Count == 0)
        {
            var leer = Make.Label("Noch keine Banken. Klicke links auf „Banken“ und füge deine erste Bank hinzu.", muted: true);
            _banken.Controls.Add(leer);
        }
    }

    private void HinweiseFuellen()
    {
        foreach (var c in _hinweise.Controls.Cast<Control>().ToList()) { _hinweise.Controls.Remove(c); c.Dispose(); }
        var liste = Finanzen.Hinweise(T, DateTime.Today);
        if (liste.Count == 0)
        {
            _hinweise.Controls.Add(new HinweisZeile(new Hinweis("Alles in Ordnung – keine Karten laufen ab und nichts braucht deine Aufmerksamkeit.", false), ok: true));
        }
        foreach (var h in liste.Take(12)) _hinweise.Controls.Add(new HinweisZeile(h));
    }

    private void Anordnen()
    {
        int pad = 0, gap = Ui.S(16);
        int w = Math.Max(Ui.S(600), ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Ui.S(4));
        int y = 0;
        int spalten = w >= Ui.S(1000) ? 4 : 2;
        int kw = (w - gap * (spalten - 1)) / spalten;
        var kz = new[] { _vermoegen, _schulden, _netto, _fix };
        for (int i = 0; i < kz.Length; i++)
        {
            int col = i % spalten, row = i / spalten;
            kz[i].Bounds = new Rectangle(pad + col * (kw + gap), y + row * (kz[i].Height + gap), kw, kz[i].Height);
        }
        y += (kz.Length / spalten) * (_vermoegen.Height + gap);
        int lw = (int)((w - gap) * 0.45);
        int h = Ui.S(290);
        _verteilung.Bounds = new Rectangle(pad, y, lw, h);
        _verlauf.Bounds = new Rectangle(pad + lw + gap, y, w - lw - gap, h);
        _donut.Bounds = new Rectangle(Ui.S(20), TitelKarte.Oben, _verteilung.Width - Ui.S(40), h - TitelKarte.Oben - Ui.S(16));
        _zeitraum.Location = new Point(_verlauf.Width - Ui.S(16) - _zeitraum.Width, Ui.S(12));
        _linie.Bounds = new Rectangle(Ui.S(12), TitelKarte.Oben + Ui.S(6), _verlauf.Width - Ui.S(28), h - TitelKarte.Oben - Ui.S(20));
        y += h + gap;

        int bh = TitelKarte.Oben;
        foreach (Control c in _banken.Controls)
        {
            c.Location = new Point(Ui.S(12), bh);
            c.Width = w - Ui.S(24);
            if (c is Label l) { l.Left = Ui.S(20); }
            bh += c.Height + Ui.S(2);
        }
        _banken.Bounds = new Rectangle(pad, y, w, bh + Ui.S(12));
        y += _banken.Height + gap;

        int hy = TitelKarte.Oben;
        foreach (Control c in _hinweise.Controls)
        {
            c.Location = new Point(Ui.S(20), hy);
            c.Width = w - Ui.S(40);
            hy += c.Height + Ui.S(4);
        }
        _hinweise.Bounds = new Rectangle(pad, y, w, hy + Ui.S(12));
        y += _hinweise.Height;
        _innen.Bounds = new Rectangle(0, AutoScrollPosition.Y, w, y + Ui.S(8));
    }
}

/// <summary>One line in the dashboard bank list: logo, name, accounts, sum, online banking.</summary>
public sealed class BankZeile : Control, IThemed
{
    private readonly MainForm _app;
    private readonly Bank _bank;
    private readonly FlatButton _online = new("Online-Banking", ButtonKind.Secondary);
    private bool _hover;

    public BankZeile(MainForm app, Bank bank)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _app = app;
        _bank = bank;
        Height = Ui.S(56);
        Cursor = Cursors.Hand;
        _online.Height = Ui.S(30);
        _online.Visible = bank.Website.Length > 0;
        _online.Click += (_, _) => _app.OnlineBanking(_bank);
        Controls.Add(_online);
        Click += (_, _) => _app.BankOeffnen(_bank);
        Resize += (_, _) => _online.Location = new Point(Width - _online.Width - Ui.S(8), (Height - _online.Height) / 2);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = ClientRectangle.Contains(PointToClient(MousePosition)); Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        var back = Ui.EffectiveBack(this);
        g.Clear(back);
        if (_hover)
        {
            using var path = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(8));
            using var b = new SolidBrush(p.SurfaceHover);
            g.FillPath(b, path);
        }
        BankLogo.Zeichnen(g, new Rectangle(Ui.S(8), Ui.S(10), Ui.S(36), Ui.S(36)), _bank.Farbe, _bank.AnzeigeKuerzel);
        TextRenderer.DrawText(g, _bank.Name, Ui.Bold, new Point(Ui.S(56), Ui.S(9)), p.Text, TextFormatFlags.NoPrefix);
        int karten = _bank.Konten.Sum(k => k.Karten.Count);
        var info = _bank.Konten.Count == 0 ? "noch keine Konten" : Wort.Anzahl(_bank.Konten.Count, "Konto", "Konten") + (karten > 0 ? " · " + Wort.Anzahl(karten, "Karte", "Karten") : "");
        TextRenderer.DrawText(g, info, Ui.Small, new Point(Ui.S(57), Ui.S(30)), p.Muted, TextFormatFlags.NoPrefix);
        int rechts = (_online.Visible ? _online.Left : Width) - Ui.S(20);
        TextRenderer.DrawText(g, _app.S.Geld(_bank.Summe), Ui.CardTitle, new Rectangle(0, 0, rechts, Height), Farben.Betrag(_bank.Summe),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

public sealed class HinweisZeile : Control, IThemed
{
    private readonly Hinweis _h;
    private readonly bool _ok;

    public HinweisZeile(Hinweis h, bool ok = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _h = h;
        _ok = ok;
        Height = Ui.S(30);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        var farbe = _ok ? p.Success : _h.Warnung ? p.Warning : p.Accent;
        TextRenderer.DrawText(g, _ok ? "\uE73E" : _h.Warnung ? Glyph.Warnung : Glyph.Info, Glyph.Font, new Rectangle(0, 0, Ui.S(24), Height), farbe,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, _h.Text, Ui.Base, new Rectangle(Ui.S(32), 0, Width - Ui.S(32), Height), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

// ============================== Banken ==============================

public sealed class BankenSeite : Seite
{
    private readonly FlowLayoutPanel _fluss = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 0, 10) };

    public BankenSeite(MainForm app) : base(app)
    {
        Controls.Add(_fluss);
    }

    public override string Titel => "Banken";
    public override string Untertitel => S.Daten == null ? "" : $"{Wort.Anzahl(T.Banken.Count, "Bank", "Banken")} · zusammen {S.Geld(Finanzen.Netto(T))}";
    public override string? AktionText => "+ Bank hinzufügen";
    public override void HauptAktion(Control anker) => App.BankNeu();

    public override void Aktualisieren()
    {
        if (S.Daten == null) return;
        _fluss.SuspendLayout();
        foreach (var c in _fluss.Controls.Cast<Control>().ToList()) { _fluss.Controls.Remove(c); c.Dispose(); }
        foreach (var b in T.Banken) _fluss.Controls.Add(new BankKachel(App, b));
        _fluss.Controls.Add(new NeueBankKachel(App));
        foreach (Control c in _fluss.Controls) c.Margin = new Padding(0, 0, Ui.S(16), Ui.S(16));
        _fluss.ResumeLayout();
        Theme.Apply(_fluss);
        App.KopfAktualisieren();
    }
}

public sealed class BankKachel : Card
{
    private readonly MainForm _app;
    private readonly Bank _bank;
    private bool _hover;

    public BankKachel(MainForm app, Bank bank)
    {
        _app = app;
        _bank = bank;
        Size = new Size(Ui.S(300), Ui.S(196));
        Cursor = Cursors.Hand;
        var oeffnen = new FlatButton("Öffnen", ButtonKind.Secondary) { Height = Ui.S(32) };
        var online = new FlatButton("Online-Banking", ButtonKind.Primary) { Height = Ui.S(32), Enabled = bank.Website.Length > 0 };
        var menue = new SymbolButton("\uE712", "Weitere Aktionen", 32);
        oeffnen.Location = new Point(Ui.S(18), Height - Ui.S(50));
        online.Location = new Point(oeffnen.Right + Ui.S(8), oeffnen.Top);
        menue.Location = new Point(Width - Ui.S(18) - menue.Width, oeffnen.Top);
        Controls.AddRange([oeffnen, online, menue]);
        oeffnen.Click += (_, _) => _app.BankOeffnen(_bank);
        online.Click += (_, _) => _app.OnlineBanking(_bank);
        Click += (_, _) => _app.BankOeffnen(_bank);
        menue.Click += (_, _) =>
        {
            var m = new ContextMenuStrip { ShowImageMargin = false };
            m.Items.Add("Öffnen", null, (_, _) => _app.BankOeffnen(_bank));
            m.Items.Add("Bearbeiten …", null, (_, _) => _app.BankBearbeiten(_bank));
            m.Items.Add("Konto hinzufügen …", null, (_, _) => _app.KontoNeu(_bank));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Nach vorne verschieben", null, (_, _) => _app.BankVerschieben(_bank, -1));
            m.Items.Add("Nach hinten verschieben", null, (_, _) => _app.BankVerschieben(_bank, 1));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Löschen …", null, (_, _) => _app.BankLoeschen(_bank));
            foreach (ToolStripItem i in m.Items) i.Font = Ui.Base;
            Optik.StyleMenu(m);
            m.Closed += (_, _) => BeginInvoke(m.Dispose);
            m.Show(menue, new Point(0, menue.Height));
        };
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = ClientRectangle.Contains(PointToClient(MousePosition)); Invalidate(); base.OnMouseLeave(e); }

    protected override void PaintContent(Graphics g)
    {
        var p = Theme.P;
        var farbe = Farben.Lesen(_bank.Farbe, p.Accent);
        using (var b = new SolidBrush(farbe))
        using (var path = Ui.Rounded(new Rectangle(Ui.S(1), Ui.S(1), Width - Ui.S(3), Ui.S(10)), Ui.S(4)))
            g.FillPath(b, path);
        using (var b = new SolidBrush(p.Surface))
            g.FillRectangle(b, Ui.S(1), Ui.S(6), Width - Ui.S(3), Ui.S(6));
        if (_hover)
        {
            using var pen = new Pen(p.Accent);
            using var path = Ui.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10));
            g.DrawPath(pen, path);
        }
        BankLogo.Zeichnen(g, new Rectangle(Ui.S(18), Ui.S(24), Ui.S(46), Ui.S(46)), _bank.Farbe, _bank.AnzeigeKuerzel);
        TextRenderer.DrawText(g, _bank.Name, Ui.CardTitle, new Rectangle(Ui.S(76), Ui.S(26), Width - Ui.S(90), Ui.S(24)), p.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        int karten = _bank.Konten.Sum(k => k.Karten.Count);
        var info = _bank.Konten.Count == 0 ? "noch keine Konten" : Wort.Anzahl(_bank.Konten.Count, "Konto", "Konten") + (karten > 0 ? " · " + Wort.Anzahl(karten, "Karte", "Karten") : "");
        TextRenderer.DrawText(g, info, Ui.Small, new Point(Ui.S(77), Ui.S(52)), p.Muted, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "Summe", Ui.Small, new Point(Ui.S(18), Ui.S(86)), p.Muted, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _app.S.Geld(_bank.Summe), Ui.Title, new Rectangle(Ui.S(16), Ui.S(102), Width - Ui.S(32), Ui.S(34)), Farben.Betrag(_bank.Summe),
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

public sealed class NeueBankKachel : Control, IThemed
{
    private bool _hover;

    public NeueBankKachel(MainForm app)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(Ui.S(300), Ui.S(196));
        Cursor = Cursors.Hand;
        Click += (_, _) => app.BankNeu();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var path = Ui.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), Ui.S(10)))
        using (var pen = new Pen(_hover ? p.Accent : p.Border, Ui.S(2)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            g.DrawPath(pen, path);
        var farbe = _hover ? p.Accent : p.Muted;
        TextRenderer.DrawText(g, Glyph.Hinzufuegen, Glyph.Gross, new Rectangle(0, Ui.S(54), Width, Ui.S(36)), farbe, TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "Bank hinzufügen", Ui.Bold, new Rectangle(0, Ui.S(98), Width, Ui.S(24)), farbe, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "z. B. DKB, N26, Volksbank …", Ui.Small, new Rectangle(0, Ui.S(122), Width, Ui.S(20)), p.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
    }
}

// ============================== Bank-Detail ==============================

public sealed class BankDetailSeite : Seite
{
    private readonly Panel _innen = new();
    public Bank? Bank { get; set; }
    private bool _pwSichtbar;

    public BankDetailSeite(MainForm app) : base(app)
    {
        AutoScroll = true;
        Controls.Add(_innen);
        Resize += (_, _) => Aktualisieren();
    }

    public override string Titel => Bank?.Name ?? "Bank";
    public override string Untertitel => Bank == null ? "" : $"{Wort.Anzahl(Bank.Konten.Count, "Konto", "Konten")} · Summe {S.Geld(Bank.Summe)}";
    public override string? AktionText => "+ Konto hinzufügen";
    public override void HauptAktion(Control anker) { if (Bank != null) App.KontoNeu(Bank); }

    public override void Aktualisieren()
    {
        if (S.Daten == null || Bank == null || Parent == null) return;
        if (!T.Banken.Contains(Bank)) { App.Zeige(App.Banken); return; }
        var scroll = AutoScrollPosition;
        SuspendLayout();
        foreach (var c in _innen.Controls.Cast<Control>().ToList()) { _innen.Controls.Remove(c); c.Dispose(); }
        int w = Math.Max(Ui.S(600), ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Ui.S(4));
        int y = 0;
        var b = Bank;

        var zurueck = new FlatButton("←  Alle Banken", ButtonKind.Ghost) { Location = new Point(0, y) };
        zurueck.Click += (_, _) => App.Zeige(App.Banken);
        var online = new FlatButton("Online-Banking öffnen", ButtonKind.Primary) { Enabled = b.Website.Length > 0 };
        online.Click += (_, _) => App.OnlineBanking(b);
        var bearbeiten = new FlatButton("Bearbeiten");
        bearbeiten.Click += (_, _) => App.BankBearbeiten(b);
        var loeschen = new FlatButton("Löschen", ButtonKind.Danger);
        loeschen.Click += (_, _) => App.BankLoeschen(b);
        loeschen.Location = new Point(w - loeschen.Width, y);
        bearbeiten.Location = new Point(loeschen.Left - Ui.S(8) - bearbeiten.Width, y);
        online.Location = new Point(bearbeiten.Left - Ui.S(8) - online.Width, y);
        _innen.Controls.AddRange([zurueck, online, bearbeiten, loeschen]);
        y += zurueck.Height + Ui.S(16);

        var info = new TitelKarte { Titel = "Zugangsdaten & Kontakt", Rechts = "verschlüsselt gespeichert" };
        int iy = TitelKarte.Oben;
        int labelW = Ui.S(170);
        void Zeile(string titel, string wert, string? kopierText = null, bool geheim = false, bool passwort = false)
        {
            if (wert.Length == 0) return;
            var l = Make.Label(titel, muted: true);
            l.Location = new Point(Ui.S(20), iy + Ui.S(6));
            var anzeige = passwort && !_pwSichtbar ? new string('•', Math.Min(12, Math.Max(8, wert.Length))) : wert;
            var v = Make.Label(anzeige, passwort ? Ui.Bold : Ui.Base, width: w - labelW - Ui.S(130));
            v.Location = new Point(Ui.S(20) + labelW, iy + Ui.S(6));
            info.Controls.Add(l);
            info.Controls.Add(v);
            int bx = w - Ui.S(20);
            if (kopierText != null)
            {
                var k = new SymbolButton(Glyph.Kopieren, $"{titel} kopieren");
                bx -= k.Width;
                k.Location = new Point(bx, iy);
                k.Click += (_, _) => App.Kopieren(kopierText, titel, geheim);
                info.Controls.Add(k);
            }
            if (passwort)
            {
                var a = new SymbolButton(_pwSichtbar ? Glyph.AugeZu : Glyph.Auge, _pwSichtbar ? "Passwort verbergen" : "Passwort anzeigen");
                bx -= a.Width + Ui.S(4);
                a.Location = new Point(bx, iy);
                a.Click += (_, _) => { _pwSichtbar = !_pwSichtbar; Aktualisieren(); };
                info.Controls.Add(a);
            }
            iy += Math.Max(Ui.S(32), v.PreferredHeight + Ui.S(12));
        }
        Zeile("Online-Banking", b.Website, b.Website);
        Zeile("Anmeldename", b.Anmeldename, b.Anmeldename);
        Zeile("Passwort", b.Passwort, b.Passwort, geheim: true, passwort: true);
        Zeile("BIC", b.Bic, b.Bic);
        Zeile("Hotline", b.Hotline, b.Hotline);
        Zeile("Ansprechpartner", b.Ansprechpartner);
        Zeile("Notizen", b.Notizen);
        if (iy == TitelKarte.Oben)
        {
            var leer = Make.Label("Noch keine Zugangsdaten eingetragen. Über „Bearbeiten“ kannst du Anmeldename, Passwort, Hotline und Notizen hinterlegen.", muted: true, width: w - Ui.S(40));
            leer.Location = new Point(Ui.S(20), iy);
            info.Controls.Add(leer);
            iy += leer.PreferredHeight + Ui.S(8);
        }
        info.Bounds = new Rectangle(0, y, w, iy + Ui.S(14));
        _innen.Controls.Add(info);
        y += info.Height + Ui.S(24);

        var kontenTitel = Make.Label("Konten", Ui.Section);
        kontenTitel.Location = new Point(0, y);
        _innen.Controls.Add(kontenTitel);
        var neu = new FlatButton("+ Konto hinzufügen");
        neu.Location = new Point(w - neu.Width, y - Ui.S(6));
        neu.Click += (_, _) => App.KontoNeu(b);
        _innen.Controls.Add(neu);
        y += Ui.S(36);
        if (b.Konten.Count == 0)
        {
            var leer = Make.Label($"Bei {b.Name} ist noch kein Konto eingetragen.", muted: true);
            leer.Location = new Point(0, y);
            _innen.Controls.Add(leer);
            y += Ui.S(30);
        }
        foreach (var k in b.Konten)
        {
            var karte = new KontoKarte(App, b, k) { Location = new Point(0, y), Width = w };
            karte.Aufbauen();
            _innen.Controls.Add(karte);
            y += karte.Height + Ui.S(12);
        }
        _innen.Bounds = new Rectangle(0, 0, w, y + Ui.S(8));
        Theme.Apply(_innen);
        ResumeLayout();
        AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        App.KopfAktualisieren();
    }
}

public sealed class KontoKarte : Card
{
    private readonly MainForm _app;
    private readonly Bank _bank;
    private readonly Konto _konto;

    public KontoKarte(MainForm app, Bank bank, Konto konto)
    {
        _app = app;
        _bank = bank;
        _konto = konto;
    }

    public void Aufbauen()
    {
        int w = Width;
        var stand = new FlatButton("Kontostand eintragen", ButtonKind.Primary) { Height = Ui.S(32) };
        var karte = new FlatButton("+ Karte") { Height = Ui.S(32) };
        var edit = new SymbolButton(Glyph.Bearbeiten, "Konto bearbeiten", 32);
        var del = new SymbolButton(Glyph.Loeschen, "Konto löschen", 32);
        int x = w - Ui.S(16);
        foreach (Control c in new Control[] { del, edit, karte, stand })
        {
            x -= c.Width;
            c.Location = new Point(x, Ui.S(70));
            x -= Ui.S(6);
            Controls.Add(c);
        }
        stand.Click += (_, _) => _app.StandEintragen(_konto);
        karte.Click += (_, _) => _app.KarteNeu(_konto);
        edit.Click += (_, _) => _app.KontoBearbeiten(_konto);
        del.Click += (_, _) => _app.KontoLoeschen(_konto);
        if (_konto.Iban.Length > 0)
        {
            var copy = new SymbolButton(Glyph.Kopieren, "IBAN kopieren", 26);
            int ibanW = TextRenderer.MeasureText(_app.S.IbanText(_konto.Iban), Ui.Base).Width;
            copy.Location = new Point(Ui.S(20) + ibanW + Ui.S(6), Ui.S(44));
            copy.Click += (_, _) => _app.Kopieren(Iban.Normalisieren(_konto.Iban), "IBAN");
            Controls.Add(copy);
        }
        int y = Ui.S(116);
        foreach (var k in _konto.Karten)
        {
            var kz = new KartenZeile(_app, _konto, k) { Location = new Point(Ui.S(12), y), Width = w - Ui.S(24) };
            Controls.Add(kz);
            y += kz.Height;
        }
        Height = y + Ui.S(12);
    }

    protected override void PaintContent(Graphics g)
    {
        var p = Theme.P;
        var s = _app.S;
        TextRenderer.DrawText(g, _konto.Name, Ui.CardTitle, new Point(Ui.S(20), Ui.S(16)), p.Text, TextFormatFlags.NoPrefix);
        int nameW = TextRenderer.MeasureText(_konto.Name, Ui.CardTitle).Width;
        var art = Texte.Name(_konto.Art);
        var artR = new Rectangle(Ui.S(24) + nameW, Ui.S(19), TextRenderer.MeasureText(art, Ui.Small).Width + Ui.S(16), Ui.S(22));
        using (var path = Ui.Rounded(artR, Ui.S(11)))
        using (var b = new SolidBrush(Ui.Blend(p.Surface, Farben.Lesen(_bank.Farbe, p.Accent), 0.22f)))
            g.FillPath(b, path);
        TextRenderer.DrawText(g, art, Ui.Small, artR, p.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        var iban = _konto.Iban.Length > 0 ? s.IbanText(_konto.Iban) : "keine IBAN eingetragen";
        TextRenderer.DrawText(g, iban, Ui.Base, new Point(Ui.S(20), Ui.S(48)), _konto.Iban.Length > 0 ? p.Text : p.Muted, TextFormatFlags.NoPrefix);
        var details = new List<string>();
        if (_konto.Inhaber.Length > 0) details.Add(_konto.Inhaber);
        if (_konto.Zinssatz is decimal z) details.Add($"{z.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))} % Zinsen");
        if (_konto.Dispo is decimal d) details.Add($"Dispo {s.Geld(d, _konto.Waehrung)}");
        if (_konto.LaufzeitBis is DateTime lz) details.Add($"läuft bis {lz:dd.MM.yyyy}");
        if (_konto.Notizen.Length > 0) details.Add(_konto.Notizen.Replace('\n', ' '));
        TextRenderer.DrawText(g, string.Join("  ·  ", details), Ui.Small, new Rectangle(Ui.S(20), Ui.S(78), Math.Max(10, Width - Ui.S(420)), Ui.S(20)), p.Muted,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, s.Geld(_konto.Kontostand, _konto.Waehrung), Ui.Title, new Rectangle(0, Ui.S(12), Width - Ui.S(20), Ui.S(34)), Farben.Betrag(_konto.Kontostand),
            TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        var vom = _konto.StandVom is DateTime t ? $"Stand {t:dd.MM.yyyy}" : "noch kein Kontostand";
        TextRenderer.DrawText(g, vom, Ui.Small, new Rectangle(0, Ui.S(46), Width - Ui.S(20), Ui.S(18)), p.Muted, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
        if (_konto.Karten.Count > 0)
        {
            using var pen = new Pen(p.Border);
            g.DrawLine(pen, Ui.S(20), Ui.S(112), Width - Ui.S(20), Ui.S(112));
        }
    }
}

public sealed class KartenZeile : Control, IThemed
{
    private readonly MainForm _app;
    private readonly Konto _konto;
    private readonly Karte _karte;

    public KartenZeile(MainForm app, Konto konto, Karte karte)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _app = app;
        _konto = konto;
        _karte = karte;
        Height = Ui.S(36);
        var edit = new SymbolButton(Glyph.Bearbeiten, "Karte bearbeiten", 28);
        var del = new SymbolButton(Glyph.Loeschen, "Karte löschen", 28);
        Controls.Add(edit);
        Controls.Add(del);
        Resize += (_, _) =>
        {
            del.Location = new Point(Width - del.Width - Ui.S(4), (Height - del.Height) / 2);
            edit.Location = new Point(del.Left - edit.Width - Ui.S(2), del.Top);
        };
        edit.Click += (_, _) => _app.KarteBearbeiten(_konto, _karte);
        del.Click += (_, _) => _app.KarteLoeschen(_konto, _karte);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        TextRenderer.DrawText(g, Glyph.Karte, Glyph.Font, new Rectangle(Ui.S(8), 0, Ui.S(24), Height), p.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        var nummer = _karte.LetzteZiffern.Length > 0 ? $"•••• {_karte.LetzteZiffern}" : "";
        var teile = new[] { _karte.Bezeichnung, _karte.Netzwerk, nummer }.Where(x => x.Length > 0);
        TextRenderer.DrawText(g, string.Join("  ·  ", teile), Ui.Base, new Rectangle(Ui.S(40), 0, Width - Ui.S(320), Height), p.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        var heute = DateTime.Today;
        var (text, farbe) = _karte.IstAbgelaufen(heute) ? ($"abgelaufen {_karte.GueltigText}", p.Danger)
            : _karte.LaeuftBaldAb(heute) ? ($"läuft ab {_karte.GueltigText}", p.Warning)
            : ($"gültig bis {_karte.GueltigText}", p.Muted);
        if (_karte.Limit is decimal l) text = $"Limit {_app.S.Geld(l, _konto.Waehrung)}  ·  " + text;
        TextRenderer.DrawText(g, text, Ui.Small, new Rectangle(0, 0, Width - Ui.S(76), Height), farbe, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

public sealed class NotrufKarte : Card
{
    protected override void PaintContent(Graphics g)
    {
        TextRenderer.DrawText(g, Glyph.Telefon, Glyph.Gross, new Rectangle(Ui.S(16), 0, Ui.S(36), Height), Theme.P.Danger, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "Karte verloren oder gestohlen?", Ui.Bold, new Point(Ui.S(62), Ui.S(15)), Theme.P.Text, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "Sofort sperren: Sperr-Notruf 116 116 (kostenlos, rund um die Uhr) · aus dem Ausland +49 116 116 · oder direkt bei deiner Bank bzw. in ihrer App.",
            Ui.Small, new Rectangle(Ui.S(62), Ui.S(38), Width - Ui.S(250), Ui.S(20)), Theme.P.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}

// ============================== Tabellen-Seiten ==============================

public abstract class TabellenSeite : Seite
{
    protected readonly DataGridView Grid = Optik.Tabelle();
    protected readonly Panel Werkzeug = new() { Dock = DockStyle.Top };
    protected readonly Panel Fuss = new() { Dock = DockStyle.Bottom };
    protected readonly Label FussText = Make.Label("", Ui.Bold);
    protected readonly InputBox Suche = new();
    private readonly Card _rahmen = new() { Dock = DockStyle.Fill, Padding = new Padding(1, Ui.S(6), 1, Ui.S(6)) };
    private readonly List<Control> _werkzeuge = [];

    protected TabellenSeite(MainForm app) : base(app)
    {
        Werkzeug.Height = Ui.S(52);
        Fuss.Height = Ui.S(40);
        Suche.Width = Ui.S(260);
        Suche.Location = new Point(0, Ui.S(4));
        Suche.Box.PlaceholderText = "Suchen …";
        Suche.Box.TextChanged += (_, _) => Aktualisieren();
        Werkzeug.Controls.Add(Suche);
        FussText.Location = new Point(Ui.S(4), Ui.S(12));
        Fuss.Controls.Add(FussText);
        _rahmen.Controls.Add(Grid);
        Controls.Add(_rahmen);
        Controls.Add(Fuss);
        Controls.Add(Werkzeug);
        Werkzeug.Layout += (_, _) =>
        {
            int x = Werkzeug.Width;
            for (int i = _werkzeuge.Count - 1; i >= 0; i--)
            {
                x -= _werkzeuge[i].Width;
                _werkzeuge[i].Location = new Point(x, Ui.S(4));
                x -= Ui.S(8);
            }
        };
        Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Bearbeiten(); };
        Grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Bearbeiten(); e.Handled = true; }
            if (e.KeyCode == Keys.Delete) { Loeschen(); e.Handled = true; }
        };
        Grid.SelectionChanged += (_, _) => AuswahlGeaendert();
        Grid.CellFormatting += (_, e) => Formatieren(e);
    }

    protected FlatButton Werkzeugknopf(string text, Action klick, ButtonKind art = ButtonKind.Secondary)
    {
        var b = new FlatButton(text, art);
        b.Click += (_, _) => klick();
        Werkzeug.Controls.Add(b);
        _werkzeuge.Add(b);
        return b;
    }

    protected object? Auswahl => Grid.CurrentRow?.Tag;
    protected virtual void Bearbeiten() { }
    protected virtual void Loeschen() { }
    protected virtual void AuswahlGeaendert() { }
    protected virtual void Formatieren(DataGridViewCellFormattingEventArgs e) { }

    protected bool Passt(params string?[] felder)
    {
        var q = Suche.Text.Trim();
        if (q.Length == 0) return true;
        return q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(w => felder.Any(f => f != null && f.Contains(w, StringComparison.CurrentCultureIgnoreCase)));
    }

    /// <summary>Refills the grid but keeps sort order, selection and scroll position.</summary>
    protected void Fuellen(IEnumerable<(object Tag, object?[] Werte)> zeilen)
    {
        var gewaehlt = Auswahl;
        int scroll = Grid.FirstDisplayedScrollingRowIndex;
        var sortSpalte = Grid.SortedColumn;
        var sortRichtung = Grid.SortOrder;
        Grid.SuspendLayout();
        Grid.Rows.Clear();
        foreach (var (tag, werte) in zeilen)
        {
            int i = Grid.Rows.Add(werte);
            Grid.Rows[i].Tag = tag;
        }
        if (sortSpalte != null && sortRichtung != SortOrder.None)
            Grid.Sort(sortSpalte, sortRichtung == SortOrder.Ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending);
        foreach (DataGridViewRow r in Grid.Rows)
            if (gewaehlt != null && ReferenceEquals(r.Tag, gewaehlt)) { r.Selected = true; Grid.CurrentCell = r.Cells[0]; }
        if (scroll >= 0 && scroll < Grid.Rows.Count) Grid.FirstDisplayedScrollingRowIndex = scroll;
        Grid.ResumeLayout();
        Theme.StyleGrid(Grid);
        AuswahlGeaendert();
        App.KopfAktualisieren();
    }

    protected void GeldFormat(DataGridViewCellFormattingEventArgs e, string waehrung = "EUR", bool vorzeichenFarbe = true)
    {
        if (e.Value is not decimal d) return;
        e.Value = S.Geld(d, waehrung);
        e.FormattingApplied = true;
        if (vorzeichenFarbe && d < 0) e.CellStyle!.ForeColor = Theme.P.Danger;
    }

    protected static void DatumFormat(DataGridViewCellFormattingEventArgs e)
    {
        if (e.Value is DateTime d) { e.Value = d.ToString("dd.MM.yyyy"); e.FormattingApplied = true; }
        else if (e.Value == null) { e.Value = "–"; e.FormattingApplied = true; }
    }
}

public sealed class KontenSeite : TabellenSeite
{
    private readonly FlatButton _stand, _edit, _del;

    public KontenSeite(MainForm app) : base(app)
    {
        Grid.Columns.AddRange(
            Optik.Spalte("bank", "Bank", 16),
            Optik.Spalte("konto", "Konto", 18),
            Optik.Spalte("art", "Art", 14),
            Optik.Spalte("iban", "IBAN", 24),
            Optik.Spalte("stand", "Kontostand", 14, rechts: true),
            Optik.Spalte("vom", "Stand vom", 10, rechts: true));
        Grid.Columns["stand"]!.ValueType = typeof(decimal);
        Grid.Columns["vom"]!.ValueType = typeof(DateTime);
        Werkzeugknopf("CSV exportieren …", Exportieren);
        _del = Werkzeugknopf("Löschen", Loeschen, ButtonKind.Danger);
        _edit = Werkzeugknopf("Bearbeiten", Bearbeiten);
        _stand = Werkzeugknopf("Kontostand eintragen", () => { if (Auswahl is Konto k) App.StandEintragen(k); }, ButtonKind.Primary);
    }

    public override string Titel => "Konten";
    public override string Untertitel => S.Daten == null ? "" : $"{Wort.Anzahl(T.AlleKonten.Count(), "Konto", "Konten")} bei {Wort.Anzahl(T.Banken.Count(b => b.Konten.Count > 0), "Bank", "Banken")}";
    public override string? AktionText => "+ Konto hinzufügen";
    public override void HauptAktion(Control anker) => App.KontoNeu(null);

    public override void Aktualisieren()
    {
        if (S.Daten == null) return;
        var zeilen = new List<(object, object?[])>();
        foreach (var b in T.Banken)
        foreach (var k in b.Konten)
            if (Passt(b.Name, k.Name, Texte.Name(k.Art), Iban.Normalisieren(k.Iban), k.Inhaber, k.Notizen))
                zeilen.Add((k, [b.Name, k.Name, Texte.Name(k.Art), S.IbanText(k.Iban), k.Kontostand, k.StandVom]));
        Fuellen(zeilen);
        var sichtbar = zeilen.Select(z => (Konto)z.Item1).ToList();
        FussText.Text = $"{Wort.Anzahl(sichtbar.Count, "Konto", "Konten")}  ·  Summe {S.Geld(sichtbar.Where(k => k.Waehrung == "EUR").Sum(k => k.Kontostand))}";
    }

    protected override void Formatieren(DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var k = Grid.Rows[e.RowIndex].Tag as Konto;
        if (e.ColumnIndex == Grid.Columns["stand"]!.Index) GeldFormat(e, k?.Waehrung ?? "EUR");
        if (e.ColumnIndex == Grid.Columns["vom"]!.Index) DatumFormat(e);
    }

    protected override void AuswahlGeaendert()
    {
        bool a = Auswahl is Konto;
        _stand.Enabled = _edit.Enabled = _del.Enabled = a;
    }

    protected override void Bearbeiten() { if (Auswahl is Konto k) App.KontoBearbeiten(k); }
    protected override void Loeschen() { if (Auswahl is Konto k) App.KontoLoeschen(k); }

    private void Exportieren()
    {
        if (MessageBox.Show(App, "Die CSV-Datei ist NICHT verschlüsselt – jeder, der sie öffnet, sieht deine Konten, IBANs und Kontostände (Passwörter sind nicht enthalten).\n\nTrotzdem exportieren?",
                "CSV exportieren", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        using var d = new SaveFileDialog { Filter = "CSV-Datei (*.csv)|*.csv", FileName = $"Konten-{DateTime.Today:yyyy-MM-dd}.csv" };
        if (d.ShowDialog(App) != DialogResult.OK || S.Daten == null) return;
        try
        {
            File.WriteAllText(d.FileName, CsvExport.Konten(T), new System.Text.UTF8Encoding(true));
            App.Status("Konten als CSV gespeichert: " + d.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(App, ex.Message, "CSV exportieren", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

public sealed class KartenSeite : TabellenSeite
{
    private readonly FlatButton _edit, _del;
    private readonly NotrufKarte _notruf = new() { Dock = DockStyle.Top };

    public KartenSeite(MainForm app) : base(app)
    {
        Grid.Columns.AddRange(
            Optik.Spalte("bank", "Bank", 14),
            Optik.Spalte("konto", "Konto", 16),
            Optik.Spalte("karte", "Karte", 18),
            Optik.Spalte("netz", "Netzwerk", 11),
            Optik.Spalte("nr", "Nummer", 10),
            Optik.Spalte("gueltig", "Gültig bis", 9, rechts: true),
            Optik.Spalte("limit", "Limit", 11, rechts: true),
            Optik.Spalte("status", "Status", 12));
        Grid.Columns["limit"]!.ValueType = typeof(decimal);
        _del = Werkzeugknopf("Löschen", Loeschen, ButtonKind.Danger);
        _edit = Werkzeugknopf("Bearbeiten", Bearbeiten);

        _notruf.Height = Ui.S(72);
        var kopieren = new FlatButton("116 116 kopieren") { Height = Ui.S(32) };
        kopieren.Click += (_, _) => App.Kopieren("116116", "Sperr-Notruf");
        _notruf.Controls.Add(kopieren);
        _notruf.Resize += (_, _) => kopieren.Location = new Point(_notruf.Width - kopieren.Width - Ui.S(18), (_notruf.Height - kopieren.Height) / 2);
        var abstand = new Panel { Dock = DockStyle.Top, Height = Ui.S(14) };
        Controls.Add(abstand);
        Controls.Add(_notruf);
        abstand.BringToFront();
        Werkzeug.BringToFront();
        Controls.SetChildIndex(_notruf, Controls.Count - 1);
        Controls.SetChildIndex(abstand, Controls.Count - 2);
    }

    public override string Titel => "Karten";
    public override string Untertitel => S.Daten == null ? "" : $"{Wort.Anzahl(T.AlleKarten.Count(), "Karte", "Karten")} · nur die letzten 4 Ziffern werden gespeichert";
    public override string? AktionText => "+ Karte hinzufügen";
    public override void HauptAktion(Control anker) => App.KarteNeu(null);

    public override void Aktualisieren()
    {
        if (S.Daten == null) return;
        var heute = DateTime.Today;
        var zeilen = new List<(object, object?[])>();
        foreach (var (b, k, c) in T.AlleKarten)
        {
            var status = c.IstAbgelaufen(heute) ? "abgelaufen" : c.LaeuftBaldAb(heute) ? "läuft bald ab" : c.LaeuftAb == null ? "" : "gültig";
            if (Passt(b.Name, k.Name, c.Bezeichnung, c.Netzwerk, c.LetzteZiffern, Texte.Name(c.Typ)))
                zeilen.Add(((k, c), [b.Name, k.Name, c.Bezeichnung, c.Netzwerk, c.LetzteZiffern.Length > 0 ? "•••• " + c.LetzteZiffern : "", c.GueltigText, c.Limit, status]));
        }
        Fuellen(zeilen);
        int bald = T.AlleKarten.Count(x => x.Karte.LaeuftBaldAb(heute) || x.Karte.IstAbgelaufen(heute));
        FussText.Text = Wort.Anzahl(zeilen.Count, "Karte", "Karten") + (bald > 0 ? $"  ·  {bald} abgelaufen oder bald ablaufend" : "");
    }

    protected override void Formatieren(DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (e.ColumnIndex == Grid.Columns["limit"]!.Index)
        {
            if (e.Value == null) { e.Value = "–"; e.FormattingApplied = true; }
            else GeldFormat(e);
        }
        if (e.ColumnIndex == Grid.Columns["status"]!.Index && e.Value is string s)
            e.CellStyle!.ForeColor = s == "abgelaufen" ? Theme.P.Danger : s == "läuft bald ab" ? Theme.P.Warning : Theme.P.Success;
    }

    protected override void AuswahlGeaendert() => _edit.Enabled = _del.Enabled = Auswahl is ValueTuple<Konto, Karte>;
    protected override void Bearbeiten() { if (Auswahl is ValueTuple<Konto, Karte> x) App.KarteBearbeiten(x.Item1, x.Item2); }
    protected override void Loeschen() { if (Auswahl is ValueTuple<Konto, Karte> x) App.KarteLoeschen(x.Item1, x.Item2); }
}

public sealed class ZahlungenSeite : TabellenSeite
{
    private readonly FlatButton _edit, _del;
    private readonly KennzahlKarte _ein = new() { Titel = "Einnahmen pro Monat", Symbol = "\uE74A" };
    private readonly KennzahlKarte _aus = new() { Titel = "Ausgaben pro Monat", Symbol = "\uE74B" };
    private readonly KennzahlKarte _rest = new() { Titel = "Bleibt übrig", Symbol = Glyph.Geld };
    private readonly Panel _kopf = new() { Dock = DockStyle.Top };

    public ZahlungenSeite(MainForm app) : base(app)
    {
        Grid.Columns.AddRange(
            Optik.Spalte("name", "Bezeichnung", 20),
            Optik.Spalte("kat", "Kategorie", 13),
            Optik.Spalte("konto", "Konto", 20),
            Optik.Spalte("betrag", "Betrag", 12, rechts: true),
            Optik.Spalte("rhythmus", "Rhythmus", 12),
            Optik.Spalte("tag", "Tag", 6, rechts: true),
            Optik.Spalte("monat", "pro Monat", 12, rechts: true),
            Optik.Spalte("aktiv", "Aktiv", 7));
        Grid.Columns["betrag"]!.ValueType = typeof(decimal);
        Grid.Columns["monat"]!.ValueType = typeof(decimal);
        _del = Werkzeugknopf("Löschen", Loeschen, ButtonKind.Danger);
        _edit = Werkzeugknopf("Bearbeiten", Bearbeiten);
        _kopf.Height = _ein.Height + Ui.S(16);
        _kopf.Controls.AddRange([_ein, _aus, _rest]);
        _kopf.Layout += (_, _) =>
        {
            int gap = Ui.S(16), w = (_kopf.Width - gap * 2) / 3;
            _ein.Bounds = new Rectangle(0, 0, w, _ein.Height);
            _aus.Bounds = new Rectangle(w + gap, 0, w, _ein.Height);
            _rest.Bounds = new Rectangle((w + gap) * 2, 0, _kopf.Width - (w + gap) * 2, _ein.Height);
        };
        Controls.Add(_kopf);
        Controls.SetChildIndex(_kopf, Controls.Count - 1);
    }

    public override string Titel => "Regelmäßige Zahlungen";
    public override string Untertitel => "Daueraufträge, Lastschriften, Abos und Gehalt – umgerechnet auf einen Monat";
    public override string? AktionText => "+ Zahlung hinzufügen";
    public override void HauptAktion(Control anker) => App.ZahlungNeu();

    public override void Aktualisieren()
    {
        if (S.Daten == null) return;
        var zeilen = new List<(object, object?[])>();
        foreach (var z in T.Zahlungen)
        {
            var konto = T.KontoMitId(z.KontoId);
            var kontoText = konto == null ? "" : $"{T.BankVon(konto)?.Name} – {konto.Name}";
            if (Passt(z.Name, z.Kategorie, kontoText, z.Notizen))
                zeilen.Add((z, [z.Name, z.Kategorie, kontoText, z.IstEinnahme ? z.Betrag : -z.Betrag, Texte.Name(z.Rhythmus), z.Tag?.ToString() ?? "", z.IstEinnahme ? z.ProMonat : -z.ProMonat, z.Aktiv ? "ja" : "pausiert"]));
        }
        Fuellen(zeilen);
        decimal ein = Finanzen.EinnahmenProMonat(T), aus = Finanzen.AusgabenProMonat(T);
        _ein.Wert = S.Geld(ein);
        _ein.SymbolFarbe = Theme.P.Success;
        _ein.Unterzeile = Wort.Anzahl(T.Zahlungen.Count(z => z.Aktiv && z.IstEinnahme), "Einnahme", "Einnahmen");
        _aus.Wert = S.Geld(aus);
        _aus.SymbolFarbe = Theme.P.Danger;
        _aus.Unterzeile = $"{Wort.Anzahl(T.Zahlungen.Count(z => z.Aktiv && !z.IstEinnahme), "Ausgabe", "Ausgaben")} · {S.Geld(aus * 12)} im Jahr";
        _rest.Wert = S.Geld(ein - aus);
        _rest.WertFarbe = ein - aus < 0 ? Theme.P.Danger : null;
        _rest.Unterzeile = "Einnahmen minus Ausgaben";
        _kopf.Invalidate(true);
        FussText.Text = Wort.Anzahl(zeilen.Count, "Zahlung", "Zahlungen");
    }

    protected override void Formatieren(DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var name = Grid.Columns[e.ColumnIndex].Name;
        if (name is "betrag" or "monat")
        {
            GeldFormat(e, vorzeichenFarbe: false);
            if (Grid.Rows[e.RowIndex].Tag is Zahlung z) e.CellStyle!.ForeColor = z.IstEinnahme ? Theme.P.Success : Theme.P.Text;
        }
        if (Grid.Rows[e.RowIndex].Tag is Zahlung { Aktiv: false }) e.CellStyle!.ForeColor = Theme.P.Muted;
    }

    protected override void AuswahlGeaendert() => _edit.Enabled = _del.Enabled = Auswahl is Zahlung;
    protected override void Bearbeiten() { if (Auswahl is Zahlung z) App.ZahlungBearbeiten(z); }
    protected override void Loeschen() { if (Auswahl is Zahlung z) App.ZahlungLoeschen(z); }
}

// ============================== Einstellungen ==============================

public sealed class EinstellungenSeite : Seite
{
    private readonly Panel _innen = new();
    private readonly Segmented _design = new("Wie Windows", "Hell", "Dunkel");
    private readonly Segmented _sperre = new("1 Min.", "5 Min.", "10 Min.", "30 Min.", "Nie");
    private readonly ToggleSwitch _verbergen = new("Beträge und IBANs verbergen (z. B. wenn jemand mitschaut)");
    private readonly ToggleSwitch _updates = new("Beim Start automatisch nach Updates suchen");
    private readonly Label _updateInfo = Make.Label("", muted: true);
    private readonly Label _pfad = Make.Label("", Ui.Small, muted: true);
    private readonly FlatButton _updateKnopf = new("Jetzt nach Updates suchen", ButtonKind.Primary);
    private static readonly int[] SperrMinuten = [1, 5, 10, 30, 0];
    private bool _laedt;

    public EinstellungenSeite(MainForm app) : base(app)
    {
        AutoScroll = true;
        Controls.Add(_innen);
        _design.SelectedIndexChanged += (_, _) =>
        {
            if (_laedt) return;
            S.Einstellungen.Design = (ThemeChoice)_design.SelectedIndex;
            S.EinstellungenSpeichern();
            Theme.Set(S.Einstellungen.Design);
        };
        _sperre.SelectedIndexChanged += (_, _) =>
        {
            if (_laedt) return;
            S.Einstellungen.AutoSperreMinuten = SperrMinuten[_sperre.SelectedIndex];
            S.EinstellungenSpeichern();
            App.Status(S.Einstellungen.AutoSperreMinuten > 0 ? $"Sperrt jetzt nach {S.Einstellungen.AutoSperreMinuten} Minuten ohne Eingabe." : "Automatische Sperre ausgeschaltet.");
        };
        _verbergen.CheckedChanged += (_, _) =>
        {
            if (_laedt) return;
            S.Einstellungen.BetraegeVerbergen = _verbergen.Checked;
            S.EinstellungenSpeichern();
        };
        _updates.CheckedChanged += (_, _) =>
        {
            if (_laedt) return;
            S.Einstellungen.UpdatesPruefen = _updates.Checked;
            S.EinstellungenSpeichern();
        };
        _updateKnopf.Click += (_, _) => App.UpdateOeffnen();
        _verbergen.Width = Ui.S(520);
        _updates.Width = Ui.S(420);
        Resize += (_, _) => Anordnen();
        Aufbauen();
    }

    public override string Titel => "Einstellungen";
    public override string Untertitel => "Design, Sicherheit, Datensicherung und Updates";

    public void UpdateStatus(UpdateInfo? verfuegbar)
    {
        _updateInfo.Text = verfuegbar == null
            ? $"Installiert: Version {Updater.CurrentVersion.ToString(3)}"
            : $"Installiert: Version {Updater.CurrentVersion.ToString(3)}  ·  Neu verfügbar: Version {verfuegbar.Version.ToString(3)}";
        _updateKnopf.Text = verfuegbar == null ? "Jetzt nach Updates suchen" : $"Update {verfuegbar.Version.ToString(3)} installieren";
        _updateKnopf.FitWidth();
    }

    private readonly List<(TitelKarte Karte, List<Control[]> Zeilen)> _karten = [];

    private void Karte(string titel, params Control[][] zeilen)
    {
        var k = new TitelKarte { Titel = titel };
        foreach (var z in zeilen) k.Controls.AddRange(z);
        _innen.Controls.Add(k);
        _karten.Add((k, zeilen.ToList()));
    }

    private FlatButton Knopf(string text, Action a, ButtonKind art = ButtonKind.Secondary)
    {
        var b = new FlatButton(text, art);
        b.Click += (_, _) => a();
        return b;
    }

    private void Aufbauen()
    {
        Karte("Aussehen", [_design]);
        Karte("Sicherheit",
            [Make.Label("Automatisch sperren nach"), _sperre],
            [_verbergen],
            [Knopf("Master-Passwort ändern …", PasswortAendern), Knopf("Jetzt sperren (Strg+L)", App.Sperren)],
            [Make.Label("Alles wird mit AES-256 verschlüsselt auf diesem PC gespeichert. Ohne dein Master-Passwort kann niemand die Daten öffnen – es gibt keine Hintertür, deshalb gut merken!", Ui.Small, muted: true, width: Ui.S(760))]);
        _pfad.MaximumSize = new Size(Ui.S(760), 0);
        Karte("Datensicherung",
            [Knopf("Sicherung speichern unter …", SicherungSpeichern, ButtonKind.Primary), Knopf("Sicherung laden …", SicherungLaden), Knopf("Datenordner öffnen", () => Ui.OpenInExplorer(S.DatenOrdner))],
            [Make.Label("Die Sicherung ist genauso verschlüsselt wie dein Tresor – speichere sie z. B. auf einem USB-Stick. Zusätzlich legt der Bank-Manager täglich automatisch eine Kopie der letzten 5 Tage an.", Ui.Small, muted: true, width: Ui.S(760))],
            [_pfad]);
        Karte("Updates", [_updateKnopf, _updateInfo], [_updates]);
        UpdateStatus(null);
    }

    public override void Aktualisieren()
    {
        _laedt = true;
        var e = S.Einstellungen;
        _design.SelectedIndex = (int)e.Design;
        int i = Array.IndexOf(SperrMinuten, e.AutoSperreMinuten);
        _sperre.SelectedIndex = i < 0 ? 2 : i;
        _verbergen.Checked = e.BetraegeVerbergen;
        _updates.Checked = e.UpdatesPruefen;
        _pfad.Text = $"Speicherort: {S.Datei.Pfad}\nAutomatische Kopien: {S.Datei.SicherungsOrdner}";
        _laedt = false;
        Anordnen();
        Invalidate(true);
    }

    private void Anordnen()
    {
        int w = Math.Max(Ui.S(600), ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Ui.S(4));
        int y = 0;
        foreach (var (karte, zeilen) in _karten)
        {
            int ky = TitelKarte.Oben;
            foreach (var zeile in zeilen)
            {
                int x = Ui.S(20), h = zeile.Max(c => c is Label l ? l.PreferredHeight : c.Height);
                foreach (var c in zeile)
                {
                    int ch = c is Label l ? l.PreferredHeight : c.Height;
                    c.Location = new Point(x, ky + (h - ch) / 2);
                    x += (c is Label lb ? lb.PreferredWidth : c.Width) + Ui.S(10);
                }
                ky += h + Ui.S(12);
            }
            karte.Bounds = new Rectangle(0, y, w, ky + Ui.S(8));
            y += karte.Height + Ui.S(16);
        }
        _innen.Bounds = new Rectangle(0, AutoScrollPosition.Y, w, y);
    }

    private void PasswortAendern()
    {
        using var d = new PasswortDialog(S);
        if (d.ShowDialog(App) == DialogResult.OK) App.Status("Master-Passwort geändert.");
    }

    private void SicherungSpeichern()
    {
        using var d = new SaveFileDialog { Filter = "Bank-Manager-Sicherung (*.bmtresor)|*.bmtresor", FileName = $"Bank-Manager-Sicherung-{DateTime.Today:yyyy-MM-dd}.bmtresor" };
        if (d.ShowDialog(App) != DialogResult.OK || S.Daten == null) return;
        try
        {
            S.Datei.Speichern(T);
            S.Datei.Exportieren(d.FileName);
            App.Status("Sicherung gespeichert: " + d.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(App, ex.Message, "Sicherung", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SicherungLaden()
    {
        using var d = new OpenFileDialog { Filter = "Bank-Manager-Sicherung (*.bmtresor)|*.bmtresor|Alle Dateien (*.*)|*.*" };
        if (d.ShowDialog(App) != DialogResult.OK || S.Daten == null) return;
        using var pw = new PasswortAbfrage("Sicherung laden", "Gib das Master-Passwort ein, mit dem diese Sicherung erstellt wurde.");
        if (pw.ShowDialog(App) != DialogResult.OK || S.Daten == null) return;
        Tresor geladen;
        try
        {
            Cursor = Cursors.WaitCursor;
            geladen = TresorDatei.Importieren(d.FileName, pw.Passwort);
        }
        catch (FalschesPasswortException) { MessageBox.Show(App, "Das Passwort passt nicht zu dieser Sicherung.", "Sicherung laden", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        catch (Exception ex) when (ex is TresorDefektException or IOException or UnauthorizedAccessException) { MessageBox.Show(App, ex.Message, "Sicherung laden", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        finally { Cursor = Cursors.Default; }
        var frage = $"Die Sicherung enthält {geladen.Banken.Count} Banken mit {geladen.AlleKonten.Count()} Konten.\n\nDeine aktuellen Daten werden dadurch ERSETZT (die bisherigen bleiben als automatische Kopie im Sicherungsordner). Fortfahren?";
        if (MessageBox.Show(App, frage, "Sicherung laden", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        T.Banken = geladen.Banken;
        T.Zahlungen = geladen.Zahlungen;
        if (S.Speichern(App)) App.Status("Sicherung geladen. Dein Master-Passwort bleibt das bisherige.");
    }
}
