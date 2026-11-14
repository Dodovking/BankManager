using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankManager.Core;

public sealed class FalschesPasswortException() : Exception("Das Master-Passwort ist falsch.");

public sealed class TresorDefektException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Encrypted data file: AES-256-GCM, key from the master password via PBKDF2-SHA256.
/// The derived key stays in memory while unlocked so saving needs no re-derivation; <see cref="Sperren"/> wipes it.
/// </summary>
public sealed class TresorDatei
{
    public const string Kennung = "BANKMANAGER-TRESOR";
    public const int StandardIterationen = 600_000;
    private const int SaltLaenge = 16, NonceLaenge = 12, TagLaenge = 16, KeyLaenge = 32;
    private const int Sicherungen = 5;

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private byte[]? _key;
    private byte[] _salt = [];
    private int _iterationen = StandardIterationen;

    public string Pfad { get; }
    public string SicherungsOrdner => Path.Combine(Path.GetDirectoryName(Pfad)!, "Sicherungen");
    public bool Existiert => File.Exists(Pfad);
    public bool Entsperrt => _key != null;

    public TresorDatei(string pfad, int iterationen = StandardIterationen)
    {
        Pfad = pfad;
        _iterationen = iterationen;
    }

    public void Anlegen(string passwort, Tresor tresor)
    {
        if (Existiert) throw new InvalidOperationException("Es gibt schon einen Tresor.");
        NeuerSchluessel(passwort);
        Speichern(tresor);
    }

    public Tresor Oeffnen(string passwort)
    {
        var (tresor, key, salt, iter) = Entschluesseln(File.ReadAllBytes(Pfad), passwort);
        _key = key;
        _salt = salt;
        _iterationen = iter;
        return tresor;
    }

    public void Speichern(Tresor tresor)
    {
        if (_key == null) throw new InvalidOperationException("Der Tresor ist gesperrt.");
        Directory.CreateDirectory(Path.GetDirectoryName(Pfad)!);
        var daten = Verschluesseln(tresor, _key, _salt, _iterationen);
        var tmp = Pfad + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(daten);
            fs.Flush(true);
        }
        if (File.Exists(Pfad))
        {
            TagesSicherung();
            File.Replace(tmp, Pfad, null);
        }
        else
        {
            File.Move(tmp, Pfad);
        }
    }

    public void PasswortAendern(string altesPasswort, string neuesPasswort, Tresor tresor)
    {
        Entschluesseln(File.ReadAllBytes(Pfad), altesPasswort);
        NeuerSchluessel(neuesPasswort);
        Speichern(tresor);
    }

    public bool PasswortPruefen(string passwort)
    {
        try
        {
            Entschluesseln(File.ReadAllBytes(Pfad), passwort);
            return true;
        }
        catch (FalschesPasswortException) { return false; }
    }

    public void Sperren()
    {
        if (_key != null) CryptographicOperations.ZeroMemory(_key);
        _key = null;
    }

    /// <summary>Reads a backup/exported file with its own password (may differ from the current one).</summary>
    public static Tresor Importieren(string pfad, string passwort) => Entschluesseln(File.ReadAllBytes(pfad), passwort).Tresor;

    public void Exportieren(string ziel) => File.Copy(Pfad, ziel, true);

    private void NeuerSchluessel(string passwort)
    {
        Sperren();
        _salt = RandomNumberGenerator.GetBytes(SaltLaenge);
        _key = Ableiten(passwort, _salt, _iterationen);
    }

    /// <summary>Keeps one copy per day of the previous file, the newest <see cref="Sicherungen"/> days.</summary>
    private void TagesSicherung()
    {
        try
        {
            Directory.CreateDirectory(SicherungsOrdner);
            var ziel = Path.Combine(SicherungsOrdner, $"tresor-{DateTime.Now:yyyy-MM-dd}.bmtresor");
            if (!File.Exists(ziel)) File.Copy(Pfad, ziel);
            foreach (var alt in Directory.GetFiles(SicherungsOrdner, "tresor-*.bmtresor").Order().Reverse().Skip(Sicherungen))
                File.Delete(alt);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static byte[] Ableiten(string passwort, byte[] salt, int iterationen) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passwort), salt, iterationen, HashAlgorithmName.SHA256, KeyLaenge);

    // Layout: "BANKMANAGER-TRESOR\n" | version(1) | iterations(4, LE) | salt(16) | nonce(12) | tag(16) | ciphertext
    private static byte[] Verschluesseln(Tresor tresor, byte[] key, byte[] salt, int iterationen)
    {
        var klartext = JsonSerializer.SerializeToUtf8Bytes(tresor, Json);
        var kopf = Kopf(iterationen, salt);
        var nonce = RandomNumberGenerator.GetBytes(NonceLaenge);
        var tag = new byte[TagLaenge];
        var geheim = new byte[klartext.Length];
        using (var aes = new AesGcm(key, TagLaenge))
            aes.Encrypt(nonce, klartext, geheim, tag, kopf);
        CryptographicOperations.ZeroMemory(klartext);
        return [.. kopf, .. nonce, .. tag, .. geheim];
    }

    private static byte[] Kopf(int iterationen, byte[] salt)
    {
        var kennung = Encoding.ASCII.GetBytes(Kennung + "\n");
        var it = BitConverter.GetBytes(iterationen);
        if (!BitConverter.IsLittleEndian) Array.Reverse(it);
        return [.. kennung, 1, .. it, .. salt];
    }

    private static (Tresor Tresor, byte[] Key, byte[] Salt, int Iterationen) Entschluesseln(byte[] datei, string passwort)
    {
        var kennung = Encoding.ASCII.GetBytes(Kennung + "\n");
        int kopfLaenge = kennung.Length + 1 + 4 + SaltLaenge;
        if (datei.Length < kopfLaenge + NonceLaenge + TagLaenge || !datei.AsSpan(0, kennung.Length).SequenceEqual(kennung))
            throw new TresorDefektException("Die Datei ist kein Bank-Manager-Tresor.");
        if (datei[kennung.Length] != 1)
            throw new TresorDefektException("Dieser Tresor stammt aus einer neueren Version des Bank-Managers. Bitte erst aktualisieren.");
        var it = datei.AsSpan(kennung.Length + 1, 4).ToArray();
        if (!BitConverter.IsLittleEndian) Array.Reverse(it);
        int iterationen = BitConverter.ToInt32(it);
        if (iterationen is < 1000 or > 50_000_000) throw new TresorDefektException("Der Tresor ist beschädigt.");
        var salt = datei.AsSpan(kennung.Length + 5, SaltLaenge).ToArray();
        var kopf = datei.AsSpan(0, kopfLaenge);
        var nonce = datei.AsSpan(kopfLaenge, NonceLaenge);
        var tag = datei.AsSpan(kopfLaenge + NonceLaenge, TagLaenge);
        var geheim = datei.AsSpan(kopfLaenge + NonceLaenge + TagLaenge);
        var key = Ableiten(passwort, salt, iterationen);
        var klartext = new byte[geheim.Length];
        try
        {
            using var aes = new AesGcm(key, TagLaenge);
            aes.Decrypt(nonce, geheim, tag, klartext, kopf);
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new FalschesPasswortException();
        }
        try
        {
            var tresor = JsonSerializer.Deserialize<Tresor>(klartext, Json) ?? new Tresor();
            return (tresor, key, salt, iterationen);
        }
        catch (JsonException e)
        {
            throw new TresorDefektException("Der Tresor ist beschädigt.", e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(klartext);
        }
    }
}

public sealed class EinstellungsDatei(string pfad)
{
    public string Pfad { get; } = pfad;

    public Einstellungen Laden()
    {
        try
        {
            if (File.Exists(Pfad))
                return JsonSerializer.Deserialize<Einstellungen>(File.ReadAllText(Pfad), TresorDatei.Json) ?? new Einstellungen();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new Einstellungen();
    }

    public void Speichern(Einstellungen e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pfad)!);
            File.WriteAllText(Pfad, JsonSerializer.Serialize(e, new JsonSerializerOptions(TresorDatei.Json) { WriteIndented = true }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public static class CsvExport
{
    /// <summary>Semicolon CSV with UTF-8 BOM so German Excel opens it directly. Contains no passwords.</summary>
    public static string Konten(Tresor t)
    {
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        var sb = new StringBuilder();
        sb.AppendLine("Bank;Konto;Art;IBAN;Inhaber;Kontostand;Währung;Stand vom;Zinssatz %;Dispo;Notizen");
        foreach (var b in t.Banken)
        foreach (var k in b.Konten)
        {
            sb.AppendLine(string.Join(';',
                Feld(b.Name), Feld(k.Name), Feld(Texte.Name(k.Art)), Feld(Iban.Formatieren(k.Iban)), Feld(k.Inhaber),
                k.Kontostand.ToString("0.00", de), k.Waehrung, k.StandVom?.ToString("dd.MM.yyyy") ?? "",
                k.Zinssatz?.ToString("0.###", de) ?? "", k.Dispo?.ToString("0.00", de) ?? "", Feld(k.Notizen)));
        }
        return sb.ToString();
    }

    private static string Feld(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@') s = "'" + s;
        return s.Contains(';') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
