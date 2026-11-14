using System.Drawing.Drawing2D;
using BankManager.Core;

namespace BankManager.Gui;

public static class Glyph
{
    public const string Home = "\uE80F", Bank = "\uE825", Liste = "\uE8FD", Karte = "\uE8C7", Wiederholen = "\uE8EE",
        Einstellungen = "\uE713", Schloss = "\uE72E", Auge = "\uE7B3", AugeZu = "\uED1A", Hinzufuegen = "\uE710",
        Kopieren = "\uE8C8", Bearbeiten = "\uE70F", Loeschen = "\uE74D", Welt = "\uE774", Zurueck = "\uE72B",
        Update = "\uE895", Warnung = "\uE7BA", Info = "\uE946", Pfeil = "\uE70D", Telefon = "\uE717", Geld = "\uE8EC";

    public static readonly Font Font = new("Segoe MDL2 Assets", 11f);
    public static readonly Font Gross = new("Segoe MDL2 Assets", 15f);
}

public static class Farben
{
    public static Color Lesen(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color TextAuf(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 160 ? Color.FromArgb(17, 24, 39) : Color.White;

    public static Color Betrag(decimal b) => b < 0 ? Theme.P.Danger : Theme.P.Text;
}

/// <summary>Colour swatch; paints itself so the theme never overwrites its colour.</summary>
public sealed class FarbFeld : Control
{
    private Color _farbe;

    public FarbFeld(Color farbe)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _farbe = farbe;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public Color Farbe { get => _farbe; set { _farbe = value; Invalidate(); } }

    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Ui.Rounded(r, Ui.S(6)))
        {
            using var b = new SolidBrush(_farbe);
            g.FillPath(b, path);
            using var pen = new Pen(Focused ? Theme.P.Accent : Theme.P.Border, Focused ? 2f : 1f);
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Farben.Hex(_farbe), Ui.Small, r, Farben.TextAuf(_farbe), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

public static class Wort
{
    public static string Anzahl(int n, string eins, string mehr) => $"{n} {(n == 1 ? eins : mehr)}";
}

public static class BankLogo
{
    public static void Zeichnen(Graphics g, Rectangle r, string farbe, string kuerzel)
    {
        var c = Farben.Lesen(farbe, Theme.P.Accent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Ui.Rounded(r, Math.Max(4, r.Width / 4)))
        using (var b = new SolidBrush(c))
            g.FillPath(b, path);
        float size = kuerzel.Length >= 3 ? r.Height * 0.27f : r.Height * 0.36f;
        using var f = new Font("Segoe UI Semibold", Math.Max(6f, size), GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, kuerzel, f, r, Farben.TextAuf(c),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>Themed drop-down: looks like an InputBox, opens a themed menu. With <see cref="Bearbeitbar"/> the text can also be typed.</summary>
public sealed class AuswahlFeld : InputBox
{
    private readonly List<(object Wert, string Text)> _eintraege = [];
    private readonly ContextMenuStrip _menu = new() { ShowImageMargin = false };
    private readonly Label _pfeil;
    private int _index = -1;

    public event EventHandler? AuswahlGeaendert;
    public bool Bearbeitbar { get; }

    public AuswahlFeld(bool bearbeitbar = false) : base(false)
    {
        Bearbeitbar = bearbeitbar;
        _pfeil = new Label { Text = Glyph.Pfeil, Font = new Font("Segoe MDL2 Assets", 8f), Dock = DockStyle.Right, Width = Ui.S(24), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, Tag = "muted", BackColor = Color.Transparent };
        Controls.Add(_pfeil);
        _pfeil.BringToFront();
        Box.ReadOnly = !bearbeitbar;
        if (!bearbeitbar)
        {
            Box.Cursor = Cursors.Hand;
            Box.MouseDown += (_, _) => Oeffnen();
            Box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Down) { Waehle(Math.Min(_index + 1, _eintraege.Count - 1)); e.Handled = true; }
                if (e.KeyCode == Keys.Up) { Waehle(Math.Max(_index - 1, 0)); e.Handled = true; }
                if (e.KeyCode is Keys.F4 or Keys.Space) { Oeffnen(); e.Handled = true; }
            };
        }
        _pfeil.MouseDown += (_, _) => Oeffnen();
        Box.TextChanged += (_, _) => { if (Bearbeitbar) AuswahlGeaendert?.Invoke(this, EventArgs.Empty); };
    }

    public void Hinzufuegen(object wert, string text) => _eintraege.Add((wert, text));

    public void Leeren()
    {
        _eintraege.Clear();
        _index = -1;
        Box.Text = "";
    }

    public int Index
    {
        get => _index;
        set => Waehle(value);
    }

    public object? Wert
    {
        get => _index >= 0 && _index < _eintraege.Count ? _eintraege[_index].Wert : null;
        set => Waehle(_eintraege.FindIndex(e => Equals(e.Wert, value)));
    }

    private void Waehle(int i)
    {
        if (i < 0 || i >= _eintraege.Count) return;
        bool neu = i != _index || Box.Text != _eintraege[i].Text;
        _index = i;
        Box.Text = _eintraege[i].Text;
        if (!Bearbeitbar && neu) AuswahlGeaendert?.Invoke(this, EventArgs.Empty);
    }

    private void Oeffnen()
    {
        if (_eintraege.Count == 0) return;
        _menu.Items.Clear();
        for (int i = 0; i < _eintraege.Count; i++)
        {
            int idx = i;
            bool aktuell = Bearbeitbar ? _eintraege[i].Text == Box.Text : i == _index;
            var item = new ToolStripMenuItem((aktuell ? "✓  " : "     ") + _eintraege[i].Text) { Font = aktuell ? Ui.Bold : Ui.Base, Padding = new Padding(0, Ui.S(3), 0, Ui.S(3)) };
            item.Click += (_, _) => { Waehle(idx); Box.Focus(); if (Bearbeitbar) Box.SelectionStart = Box.TextLength; };
            _menu.Items.Add(item);
        }
        Optik.StyleMenu(_menu);
        _menu.MinimumSize = new Size(Width, 0);
        _menu.MaximumSize = new Size(Math.Max(Width, Ui.S(200)), Ui.S(420));
        _menu.Show(this, new Point(0, Height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        _pfeil.ForeColor = Theme.P.Muted;
        base.OnPaint(e);
    }
}

/// <summary>Left navigation entry with icon.</summary>
public sealed class NavButton : Control, IThemed
{
    private bool _hover;
    public string Symbol { get; }
    public bool Aktiv { get => _aktiv; set { _aktiv = value; Invalidate(); } }
    private bool _aktiv;

    public NavButton(string symbol, string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        Symbol = symbol;
        Text = text;
        Font = Ui.Base;
        Height = Ui.S(40);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(Ui.S(10), 0, Width - Ui.S(20), Height);
        if (_aktiv || _hover)
        {
            using var path = Ui.Rounded(r, Ui.S(8));
            using var b = new SolidBrush(_aktiv ? Ui.Blend(Ui.EffectiveBack(this), p.Accent, 0.22f) : p.SurfaceHover);
            g.FillPath(b, path);
        }
        if (_aktiv)
        {
            using var b = new SolidBrush(p.Accent);
            using var bar = Ui.Rounded(new Rectangle(r.X, r.Y + Ui.S(10), Ui.S(3), r.Height - Ui.S(20)), 1);
            g.FillPath(b, bar);
        }
        var color = _aktiv ? p.Text : Ui.Blend(p.Text, p.Muted, 0.4f);
        TextRenderer.DrawText(g, Symbol, Glyph.Font, new Rectangle(r.X + Ui.S(12), 0, Ui.S(24), Height), _aktiv ? p.Accent : color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Text, _aktiv ? Ui.Bold : Ui.Base, new Rectangle(r.X + Ui.S(46), 0, r.Width - Ui.S(50), Height), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(p.Accent) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, Rectangle.Inflate(r, -2, -2));
        }
    }
}

/// <summary>Small square button with an MDL2 glyph and a tooltip.</summary>
public sealed class SymbolButton : Control, IThemed
{
    private static readonly ToolTip Tip = new();
    private bool _hover;
    private string _symbol;
    public Color? Farbe { get; set; }

    public SymbolButton(string symbol, string tooltip, int size = 30)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        _symbol = symbol;
        Size = new Size(Ui.S(size), Ui.S(size));
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = tooltip;
        Tip.SetToolTip(this, tooltip);
    }

    public string Symbol { get => _symbol; set { _symbol = value; Invalidate(); } }
    public void SetTooltip(string text) { Tip.SetToolTip(this, text); AccessibleName = text; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        var back = Ui.EffectiveBack(this);
        g.Clear(back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        if (_hover)
        {
            using var path = Ui.Rounded(r, Ui.S(6));
            using var b = new SolidBrush(p.SurfaceHover);
            g.FillPath(b, path);
        }
        if (Focused && ShowFocusCues)
        {
            using var path = Ui.Rounded(r, Ui.S(6));
            using var pen = new Pen(p.Accent) { DashStyle = DashStyle.Dot };
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, _symbol, Glyph.Font, r, Farbe ?? (_hover ? p.Text : p.Muted),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Key figure tile: caption, big value, small sub line.</summary>
public sealed class KennzahlKarte : Card
{
    public string Titel { get; set; } = "";
    public string Wert { get; set; } = "";
    public string Unterzeile { get; set; } = "";
    public string Symbol { get; set; } = Glyph.Geld;
    public Color? WertFarbe { get; set; }
    public Color? SymbolFarbe { get; set; }

    public KennzahlKarte() { Height = Ui.S(112); }

    protected override void PaintContent(Graphics g)
    {
        var p = Theme.P;
        int pad = Ui.S(18);
        var sym = SymbolFarbe ?? p.Accent;
        var badge = new Rectangle(Width - pad - Ui.S(34), pad, Ui.S(34), Ui.S(34));
        using (var path = Ui.Rounded(badge, Ui.S(8)))
        using (var b = new SolidBrush(Ui.Blend(p.Surface, sym, 0.18f)))
            g.FillPath(b, path);
        TextRenderer.DrawText(g, Symbol, Glyph.Font, badge, sym, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Titel, Ui.Base, new Point(pad, pad), p.Muted, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Wert, Ui.Title, new Rectangle(pad, pad + Ui.S(24), Width - pad * 2, Ui.S(34)), WertFarbe ?? p.Text,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g, Unterzeile, Ui.Small, new Rectangle(pad, pad + Ui.S(60), Width - pad * 2, Ui.S(20)), p.Muted,
            TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

public sealed class DonutDiagramm : Control, IThemed
{
    public List<(string Text, decimal Wert, Color Farbe)> Teile { get; } = [];
    public Func<decimal, string> Format { get; set; } = d => Finanzen.Euro(d);
    public string LeerText { get; set; } = "Noch keine Kontostände";

    public DonutDiagramm()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int d = Math.Min(Height, Ui.S(190));
        var ring = new Rectangle(Ui.S(4), (Height - d) / 2, d - Ui.S(8), d - Ui.S(8));
        float dicke = Ui.S(22);
        var innen = Rectangle.Inflate(ring, -(int)(dicke / 2), -(int)(dicke / 2));
        var teile = Teile.Where(t => t.Wert > 0).ToList();
        decimal summe = teile.Sum(t => t.Wert);
        if (summe <= 0)
        {
            using var pen = new Pen(p.Border, dicke);
            g.DrawEllipse(pen, innen);
            TextRenderer.DrawText(g, LeerText, Ui.Small, ring, p.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
        else
        {
            float start = -90;
            foreach (var t in teile)
            {
                float sweep = (float)(t.Wert / summe * 360m);
                using var pen = new Pen(t.Farbe, dicke);
                g.DrawArc(pen, innen, start, Math.Max(0.5f, sweep - (teile.Count > 1 ? 1.2f : 0)));
                start += sweep;
            }
            var mitte = Rectangle.Inflate(innen, -(int)(dicke / 2), -(int)(dicke / 2));
            TextRenderer.DrawText(g, "Guthaben", Ui.Small, new Rectangle(mitte.X, mitte.Y + mitte.Height / 2 - Ui.S(22), mitte.Width, Ui.S(18)), p.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Format(summe), Ui.Bold, new Rectangle(mitte.X - Ui.S(4), mitte.Y + mitte.Height / 2 - Ui.S(2), mitte.Width + Ui.S(8), Ui.S(22)), p.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
        int x = ring.Right + Ui.S(22), y = Math.Max(0, (Height - Math.Min(Teile.Count, 8) * Ui.S(28)) / 2);
        foreach (var t in Teile.Take(8))
        {
            using (var b = new SolidBrush(t.Farbe))
                g.FillEllipse(b, x, y + Ui.S(6), Ui.S(10), Ui.S(10));
            int restW = Width - x - Ui.S(18);
            var wert = Format(t.Wert);
            int ww = TextRenderer.MeasureText(wert, Ui.Base).Width;
            TextRenderer.DrawText(g, t.Text, Ui.Base, new Rectangle(x + Ui.S(18), y, Math.Max(10, restW - ww - Ui.S(8)), Ui.S(22)), p.Text,
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, wert, Ui.Base, new Rectangle(x, y, Width - x - Ui.S(4), Ui.S(22)), Farben.Betrag(t.Wert),
                TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            y += Ui.S(28);
        }
    }
}

public sealed class LinienDiagramm : Control, IThemed
{
    public List<(DateTime Datum, decimal Wert)> Punkte { get; } = [];
    public Func<decimal, string> Format { get; set; } = d => Finanzen.Euro(d);
    public bool Verborgen { get; set; }

    public LinienDiagramm()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var p = Theme.P;
        var g = e.Graphics;
        g.Clear(Ui.EffectiveBack(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Punkte.Count < 2)
        {
            TextRenderer.DrawText(g, "Noch zu wenig Daten. Trage bei deinen Konten ab und zu den Kontostand ein – dann entsteht hier dein Verlauf.",
                Ui.Small, new Rectangle(Ui.S(20), 0, Width - Ui.S(40), Height), p.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return;
        }
        decimal min = Punkte.Min(x => x.Wert), max = Punkte.Max(x => x.Wert);
        if (min == max) { min -= 1; max += 1; }
        decimal spanne = max - min;
        min -= spanne * 0.08m;
        max += spanne * 0.08m;
        int links = Verborgen ? Ui.S(8) : Ui.S(92), unten = Ui.S(24);
        var plot = new Rectangle(links, Ui.S(8), Width - links - Ui.S(12), Height - unten - Ui.S(10));
        using (var grid = new Pen(Ui.Blend(p.Surface, p.Border, 0.7f)) { DashStyle = DashStyle.Dash })
        {
            for (int i = 0; i <= 3; i++)
            {
                int y = plot.Bottom - plot.Height * i / 3;
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                if (!Verborgen)
                {
                    var wert = min + (max - min) * i / 3;
                    TextRenderer.DrawText(g, Format(Math.Round(wert, 0)), Ui.Small, new Rectangle(0, y - Ui.S(9), links - Ui.S(8), Ui.S(18)), p.Muted,
                        TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
            }
        }
        long t0 = Punkte[0].Datum.Ticks, t1 = Punkte[^1].Datum.Ticks;
        if (t1 == t0) t1 = t0 + 1;
        PointF Pos((DateTime d, decimal w) pt) => new(
            plot.Left + (float)((pt.d.Ticks - t0) / (double)(t1 - t0)) * plot.Width,
            plot.Bottom - (float)((pt.w - min) / (max - min)) * plot.Height);
        var pts = Punkte.Select(Pos).ToArray();
        using (var fill = new GraphicsPath())
        {
            fill.AddLines(pts);
            fill.AddLine(pts[^1], new PointF(pts[^1].X, plot.Bottom));
            fill.AddLine(new PointF(pts[^1].X, plot.Bottom), new PointF(pts[0].X, plot.Bottom));
            fill.CloseFigure();
            using var brush = new LinearGradientBrush(new Rectangle(plot.X, plot.Y - 1, plot.Width, plot.Height + 2),
                Color.FromArgb(90, p.Accent), Color.FromArgb(0, p.Accent), LinearGradientMode.Vertical);
            g.FillPath(brush, fill);
        }
        using (var pen = new Pen(p.Accent, Ui.S(2)) { LineJoin = LineJoin.Round })
            g.DrawLines(pen, pts);
        using (var dot = new SolidBrush(p.Accent))
            g.FillEllipse(dot, pts[^1].X - Ui.S(4), pts[^1].Y - Ui.S(4), Ui.S(8), Ui.S(8));
        var fmt = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, Punkte[0].Datum.ToString("dd.MM.yy"), Ui.Small, new Point(plot.Left, plot.Bottom + Ui.S(6)), p.Muted, fmt);
        var ende = Punkte[^1].Datum.ToString("dd.MM.yy");
        TextRenderer.DrawText(g, ende, Ui.Small, new Point(plot.Right - TextRenderer.MeasureText(ende, Ui.Small).Width, plot.Bottom + Ui.S(6)), p.Muted, fmt);
    }
}

public static class Optik
{
    public static void StyleMenu(ToolStrip menu)
    {
        menu.Renderer = new MenuRenderer();
        menu.BackColor = Theme.P.Surface;
        menu.ForeColor = Theme.P.Text;
        foreach (ToolStripItem i in menu.Items) i.ForeColor = Theme.P.Text;
    }

    private sealed class MenuRenderer() : ToolStripProfessionalRenderer(new MenuColors())
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new Rectangle(Ui.S(3), 1, e.Item.Width - Ui.S(6), e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Ui.Rounded(r, Ui.S(5));
            using var b = new SolidBrush(Theme.P.Selection);
            e.Graphics.FillPath(b, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.P.Text : Theme.P.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.P.Surface;
        public override Color ImageMarginGradientBegin => Theme.P.Surface;
        public override Color ImageMarginGradientMiddle => Theme.P.Surface;
        public override Color ImageMarginGradientEnd => Theme.P.Surface;
        public override Color MenuBorder => Theme.P.Border;
        public override Color MenuItemBorder => Theme.P.Selection;
        public override Color MenuItemSelected => Theme.P.Selection;
        public override Color MenuItemSelectedGradientBegin => Theme.P.Selection;
        public override Color MenuItemSelectedGradientEnd => Theme.P.Selection;
        public override Color MenuItemPressedGradientBegin => Theme.P.Selection;
        public override Color MenuItemPressedGradientEnd => Theme.P.Selection;
        public override Color SeparatorDark => Theme.P.Border;
        public override Color SeparatorLight => Theme.P.Surface;
        public override Color CheckBackground => Theme.P.Selection;
        public override Color CheckSelectedBackground => Theme.P.Selection;
        public override Color CheckPressedBackground => Theme.P.Selection;
    }

    public static DataGridView Tabelle()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = Ui.S(38),
            Font = Ui.Base,
            BorderStyle = BorderStyle.None,
            StandardTab = true,
        };
        g.RowTemplate.Height = Ui.S(38);
        g.DefaultCellStyle.Padding = new Padding(Ui.S(8), 0, Ui.S(8), 0);
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(Ui.S(8), 0, Ui.S(8), 0);
        g.ColumnHeadersDefaultCellStyle.Font = Ui.Bold;
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(g, true);
        Theme.StyleGrid(g);
        return g;
    }

    public static DataGridViewTextBoxColumn Spalte(string name, string titel, float gewicht, bool rechts = false)
    {
        var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = titel, FillWeight = gewicht, SortMode = DataGridViewColumnSortMode.Automatic };
        if (rechts)
        {
            c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        return c;
    }
}

public static class Zwischenablage
{
    private static System.Windows.Forms.Timer? _timer;

    /// <summary>Copies text; secrets are removed from the clipboard again after 30 seconds (if still there).</summary>
    public static bool Kopieren(string text, bool geheim)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try { Clipboard.SetText(text); }
        catch { return false; }
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        if (geheim)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 30_000 };
            _timer.Tick += (_, _) =>
            {
                _timer?.Stop();
                try { if (Clipboard.ContainsText() && Clipboard.GetText() == text) Clipboard.Clear(); } catch { }
            };
            _timer.Start();
        }
        return true;
    }
}
