using BankManager.Core;

namespace BankManager.Gui;

/// <summary>Everything the windows share: the vault file, the unlocked data and the settings.</summary>
public sealed class Sitzung
{
    public string DatenOrdner { get; }
    public TresorDatei Datei { get; }
    public EinstellungsDatei EinstellungsDatei { get; }
    public Einstellungen Einstellungen { get; }
    public Tresor? Daten { get; private set; }

    public event Action? Geaendert;

    public Sitzung(string? datenOrdner)
    {
        DatenOrdner = datenOrdner ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BankManager");
        Datei = new TresorDatei(Path.Combine(DatenOrdner, "tresor.bmtresor"));
        EinstellungsDatei = new EinstellungsDatei(Path.Combine(DatenOrdner, "einstellungen.json"));
        Einstellungen = EinstellungsDatei.Laden();
    }

    public Tresor T => Daten ?? throw new InvalidOperationException("Der Tresor ist gesperrt.");

    public void Entsperrt(Tresor daten)
    {
        Daten = daten;
        Geaendert?.Invoke();
    }

    public void Sperren()
    {
        Datei.Sperren();
        Daten = null;
    }

    public bool Speichern(IWin32Window? owner = null)
    {
        if (Daten == null) return false;
        try
        {
            Datei.Speichern(Daten);
            Geaendert?.Invoke();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, "Die Daten konnten nicht gespeichert werden:\n\n" + e.Message, "Bank-Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    public void EinstellungenSpeichern() => EinstellungsDatei.Speichern(Einstellungen);

    public string Geld(decimal betrag, string waehrung = "EUR") =>
        Einstellungen.BetraegeVerbergen ? "••••• " + (waehrung == "EUR" ? "€" : waehrung) : Finanzen.Euro(betrag, waehrung);

    public string IbanText(string iban) => Einstellungen.BetraegeVerbergen ? Iban.Maskieren(iban) : Iban.Formatieren(iban);
}
