# Bank-Manager

Windows-Programm, um alle Banken an einem Ort zu verwalten – ING-DiBa, Crédit Agricole, CosmosDirekt, Bank11, Sparkasse, Klarna und beliebige weitere.

- **Übersicht:** Guthaben, Schulden, Nettovermögen, Fixkosten, Verteilung nach Bank, Verlauf, Hinweise (Karten laufen ab, Festgeld endet, Dispo …)
- **Banken:** Logo-Kachel in Bankfarbe, Online-Banking mit einem Klick, Zugangsdaten (Anmeldename/Passwort) mit Kopieren-Knopf (Passwort wird nach 30 s aus der Zwischenablage gelöscht)
- **Konten:** Girokonto, Tagesgeld, Festgeld, Depot, Kredit/Ratenkauf … mit IBAN-Prüfung, Kontostand-Verlauf, Zinsen, Dispo, Laufzeit; CSV-Export
- **Karten:** nur die letzten 4 Ziffern, Ablaufdatum mit Warnung, Sperr-Notruf 116 116
- **Regelmäßige Zahlungen:** Gehalt, Miete, Abos, Raten – umgerechnet auf den Monat
- **Sicherheit:** alles AES-256-GCM-verschlüsselt (Schlüssel per PBKDF2-SHA256, 600 000 Runden) in `%APPDATA%\BankManager`, Master-Passwort, automatische Sperre, Beträge verbergen, tägliche Sicherungskopie
- **Updates:** prüft beim Start auf GitHub, fragt nach und aktualisiert sich selbst (Daten bleiben erhalten)

## Installieren

PowerShell öffnen und einfügen:

```powershell
irm https://raw.githubusercontent.com/Dodovking/BankManager/main/install.ps1 | iex
```

## Entwickeln

```bash
dotnet test                       # Kern-Tests (laufen auch unter Linux)
tools/release.sh "Was ist neu"    # EXE bauen + GitHub-Release (Version in src/BankManager.Gui/BankManager.Gui.csproj)
```

`BankManager.exe --screenshots <ordner>` rendert alle Seiten und Dialoge mit Demodaten als PNG (fasst den echten Tresor nicht an).
