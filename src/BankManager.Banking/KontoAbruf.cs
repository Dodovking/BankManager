using BankManager.Core;
using libfintx.FinTS;
using libfintx.FinTS.BankParameterData;
using libfintx.FinTS.Data;
using libfintx.Globals;
using Microsoft.Extensions.Logging.Abstractions;

namespace BankManager.Banking;

/// <summary>What the bank wants from the user: approve in the banking app, or type a TAN.</summary>
public sealed record Freigabe(bool InAppFreigeben, string Hinweis);

public sealed record AbrufErgebnis(bool Ok, decimal? Kontostand, string Meldung);

/// <summary>
/// Reads account balances via FinTS (HKSAL) directly from the bank. Only read-only business transactions are used here –
/// this program never sends transfers or other orders. No log files; bank parameter data stays in memory, user parameter data only in a temp folder deleted after each call.
/// </summary>
public sealed class KontoAbruf
{
    private readonly string _produktnummer;
    private readonly string _version;

    public KontoAbruf(string produktnummer, string version)
    {
        _produktnummer = produktnummer.Trim();
        _version = version;
    }

    public static string? KontonummerAus(string iban)
    {
        var n = Iban.Normalisieren(iban);
        return n.Length == 22 && n.StartsWith("DE") ? n[12..].TrimStart('0') : null;
    }

    public async Task<AbrufErgebnis> SaldoAbrufen(Bank bank, Konto konto, string pin, Func<Freigabe, Task<string?>> tanAnfrage, CancellationToken ct)
    {
        var z = bank.FinTs;
        if (z == null || !z.Aktiv) return new(false, null, "Automatischer Abruf ist für diese Bank nicht eingerichtet.");
        if (_produktnummer.Length == 0) return new(false, null, "Es fehlt noch die FinTS-Produktnummer (Einstellungen → Automatischer Abruf).");
        if (!Uri.TryCreate(z.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return new(false, null, "Die FinTS-Adresse muss mit https:// beginnen.");
        var blzText = z.Blz.Length > 0 ? z.Blz : Iban.Bankleitzahl(konto.Iban) ?? "";
        if (!int.TryParse(blzText, out var blz)) return new(false, null, "Bankleitzahl fehlt.");
        var kontonr = KontonummerAus(konto.Iban);
        if (kontonr == null) return new(false, null, "Für den Abruf braucht das Konto eine deutsche IBAN.");

        FinTsGlobals.ProductId = _produktnummer;
        FinTsGlobals.Version = _version;
        var details = new ConnectionDetails
        {
            Url = z.Url,
            Blz = blz,
            UserId = z.Benutzerkennung,
            Pin = pin,
            Iban = Iban.Normalisieren(konto.Iban),
            Bic = bank.Bic,
            Account = kontonr,
            CustomerSystemId = z.KundensystemId,
        };
        // The library keeps user parameter data in files under ProgramBaseDir: use a private temp folder and delete it right after.
        var tmp = Path.Combine(Path.GetTempPath(), "BankManager-FinTS-" + Guid.NewGuid().ToString("N"));
        FinTsGlobals.ProgramBaseDir = tmp;
        var client = new FinTsClient(details, false, new BdpInMemoryStore(), NullLoggerFactory.Instance);
        TANDialog? dialog = null;
        dialog = new TANDialog(async d =>
        {
            if (ct.IsCancellationRequested) { d.IsCancelWaitForApproval = true; return null!; }
            var hinweis = string.Join("\n", d.DialogResult?.Messages?.Select(m => m.Message).Where(m => !string.IsNullOrWhiteSpace(m)) ?? []);
            bool app = d.DialogResult?.IsApprovalRequired == true;
            var tan = await tanAnfrage(new Freigabe(app, hinweis));
            if (ct.IsCancellationRequested) d.IsCancelWaitForApproval = true;
            return tan!;
        });
        using var reg = ct.Register(() => { if (dialog != null) dialog.IsCancelWaitForApproval = true; });
        try
        {
            var r = await client.Balance(dialog);
            if (ct.IsCancellationRequested) return new(false, null, "Abgebrochen.");
            if (!r.IsSuccess || r.Data == null)
            {
                var text = string.Join(" · ", r.Messages.Where(m => !m.IsSuccess).Select(m => $"{m.Message} ({m.Code})"));
                return new(false, null, text.Length > 0 ? text : "Die Bank hat den Abruf abgelehnt.");
            }
            if (!string.IsNullOrEmpty(client.SystemId)) z.KundensystemId = client.SystemId;
            return new(true, r.Data.Balance, "OK");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or FormatException)
        {
            return new(false, null, "Verbindung zur Bank fehlgeschlagen: " + e.Message);
        }
        catch (Exception e)
        {
            return new(false, null, "Fehler beim Abruf: " + (e.InnerException?.Message ?? e.Message));
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
    }
}
