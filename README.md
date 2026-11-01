# Klausurfix

**Klausurfix** ist eine spezialisierte, datenschutzkonforme Offline-Anwendung für Lehrerinnen und Lehrer zur Verwaltung von Klassen, Klausuren und Noten, mit automatischer und transparenter Rechtschreibfehler-Abzugsberechnung (Fehlerquotient) und grafischem Notenspiegel.

Entwickelt für den Betrieb auf einem **Microsoft Surface Pro** unter Windows.

---

## Hauptmerkmale

- **100 % Lokal & Offline:** Keine Cloud-Verbindung, keine Registrierung, keine Telemetrie, keine externen CDNs oder Schriftarten.
- **Echte Verschlüsselung (AES-256-GCM):** Alle personenbezogenen Schülerdaten (Namen, Noten, Punkte, Notizen) und Sicherungsdateien werden mit 256-Bit-Schlüsseln und authentifizierter Verschlüsselung geschützt.
- **Surface & Touch-optimiert:** Großzügige Touch-Ziele (mind. 44 px), feste Tabellenköpfe, fixierte Schülernamenspalte bei breiten Tabellen, Tab- und Enter-Tastaturnavigation sowie Unterstützung für deutsches Dezimalkomma (`2,5`).
- **Flexible Schulformen & Bewertungsprofile:**
  - Wahlweise **Schulnoten (1–6)** oder **Oberstufen-Notenpunkte (0–15)**.
  - Frei definierbare Fehlerquotient-Abzugstabelle: Stufen ab Grenze einschließlich bis zur nächsten Grenze ausschließlich.
  - Prozentualer Notenschlüssel mit exakter Grenzprüfung ohne Vorab-Rundung.
- **Klausur-Snapshotting:** Jede Klausur friert ihr Regelwerk und ihre Schülerliste bei Erstellung ein. Spätere Stammdatenänderungen verändern historische Klausuren niemals stillschweigend.
- **Saubere Status-Trennung:** Regulär bewertete Schüler, tatsächlich 0 Punkte, unvollständige und krankheitsbedingt fehlende Schüler werden exakt unterschieden; Abwesende werden im Klassenschnitt nicht als 0 gewertet.
- **PDF- & DOCX-Export:** Generierung ansprechender Übersichtsberichte im Querformat mit vollständiger Aufgabenmatrix, Notenspiegel und Bewertungsregeln.
- **Sicherheit & Windows Hello:**
  - 6-stellige App-PIN mit PBKDF2-Schlüsselableitung (600.000 Iterationen) und progressiver Verzögerung bei Fehlversuchen.
  - Windows Hello / DPAPI für schnelles biometrisches Entsperren.
  - Unabhängiger Wiederherstellungsschlüssel (`KF-XXXX-XXXX-...`) für portable Backups und Neuinstallationen.
  - Automatische Sperre bei Inaktivität und manueller Sofort-Sperrknopf.

---

## Schnellstart

### 1. Starten
Doppelklick auf:
```cmd
start-klausurfix.cmd
```
Oder im Terminal:
```bash
dotnet run --project src/Klausurfix.Web/Klausurfix.Web.csproj
```
Klausurfix bindet ausschließlich an den lokalen Loopback-Adapter (`http://localhost:5000`) und öffnet automatisch Microsoft Edge bzw. Ihren Standardbrowser.

### 2. Ersteinrichtung
1. Beim ersten Start öffnet sich der Einrichtungs-Assistent.
2. Legen Sie Ihre **App-PIN** fest (mindestens 6 Ziffern).
3. Notieren Sie sich den generierten **Wiederherstellungsschlüssel**.
4. Nach Bestätigung startet Klausurfix direkt in die Übersicht.

---

## Projektstruktur

```
Klausurfix/
├── Klausurfix.sln
├── start-klausurfix.cmd               # Start-Skript für den Windows-Desktop
├── AGENTS.md                          # Projektspezifikation und Vorgaben
├── README.md
├── src/
│   ├── Klausurfix.Core/              # Fachlogik, Domänenmodelle, exakter Berechnungs-Core
│   │   ├── Models/                   # Schulform, GradingProfile, Exam, Student, Tiers
│   │   └── Calculations/             # ExamCalculator (FQ, Abzüge, Notenspiegel, Schnitte)
│   ├── Klausurfix.Infrastructure/    # Sicherheit, Speicher, Export
│   │   ├── Security/                 # AES-GCM, PBKDF2, DPAPI, KeyManager
│   │   ├── Storage/                  # SQLite WAL Context, AppPaths (%LOCALAPPDATA%)
│   │   ├── Backup/                   # Encrypted Backup (.kfbak), Restore, Cleanup
│   │   └── Export/                   # QuestPDF (PDF) & OpenXml (DOCX)
│   └── Klausurfix.Web/               # ASP.NET Core Server & Offline-SPA
│       ├── Program.cs                # Loopback REST APIs & Security Middleware
│       └── wwwroot/                  # Modernes Frontend (HTML5, ES Modules, CSS)
└── tests/
    └── Klausurfix.Tests/             # 25 automatische xUnit Unit- & Integrationstests
```

---

## Tests ausführen

Alle fachlichen Berechnungen, Sicherheitsfunktionen, Datenbank-Rundläufe und Exportfunktionen sind durch automatisierte Tests abgedeckt:

```bash
dotnet test
```

