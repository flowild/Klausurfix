# Klausurfix

**Klausurfix** ist eine spezialisierte, datenschutzkonforme Offline-Anwendung für Lehrerinnen und Lehrer zur Verwaltung von Klassen, Klausuren und Noten, mit automatischer und transparenter Rechtschreibfehler-Abzugsberechnung (Fehlerquotient) und grafischem Notenspiegel.

Entwickelt für den Betrieb auf einem **Microsoft Surface Pro** unter Windows.

---

## Hauptmerkmale

- **100 % Lokal & Offline:** Keine Cloud-Verbindung, keine Registrierung, keine Telemetrie, keine externen CDNs oder Schriftarten.
- **Echte Verschlüsselung (AES-256-GCM):** Alle personenbezogenen Schülerdaten (Namen, Noten, Punkte, Notizen) und Sicherungsdateien werden mit 256-Bit-Schlüsseln und authentifizierter Verschlüsselung geschützt.
- **Surface & Touch-optimiert:** Großzügige Touch-Ziele (mind. 44 px), feste Tabellenköpfe, fixierte Schülernamenspalte bei breiten Tabellen, Tab- und Enter-Tastaturnavigation sowie Unterstützung für deutsches Dezimalkomma (`2,5`).
- **Darstellung und Navigation:** Umschaltbarer Petrol-Darkmode; der Browser merkt sich die Auswahl. Klassenlisten lassen sich einklappen und die Zurück-/Vorwärts-Tasten führen durch die Ansichten.
- **Schülererfassung:** Namen beginnen mit dem Vornamen. Nach dem Speichern bleibt das Eingabefenster für den nächsten Schüler geöffnet.
- **Flexible Schulformen & Bewertungsprofile:**
  - Wahlweise **Schulnoten (1–6)** oder **Oberstufen-Notenpunkte (0–15)**.
  - Frei definierbare Fehlerquotient-Abzugstabelle: Stufen ab Grenze einschließlich bis zur nächsten Grenze ausschließlich.
  - Prozentualer Notenschlüssel mit exakter Grenzprüfung ohne Vorab-Rundung.
- **Klausur-Snapshotting:** Jede Klausur friert ihr Regelwerk und ihre Schülerliste bei Erstellung ein. Spätere Stammdatenänderungen verändern historische Klausuren niemals stillschweigend.
- **Saubere Status-Trennung:** Regulär bewertete Schüler, tatsächlich 0 Punkte, unvollständige und krankheitsbedingt fehlende Schüler werden exakt unterschieden; Abwesende werden im Klassenschnitt nicht als 0 gewertet.
- **PDF- & DOCX-Export:** Übersichtsberichte im Querformat mit Aufgabenmatrix, waagerechtem Notenspiegel und Bewertungsregeln.
- **Sicherheit & Windows Hello:**
  - 6-stellige App-PIN mit PBKDF2-Schlüsselableitung (600.000 Iterationen) und progressiver Verzögerung bei Fehlversuchen.
  - Die PIN-Schlüsselhülle wird zusätzlich an den Windows-Benutzer gebunden. Die Geräteanmeldung über WebAuthn erfordert Benutzerverifikation und einen PRF-fähigen Plattformauthentifikator; die App-PIN bleibt verfügbar. Die Funktion muss auf dem Surface mit Windows Hello und dem verwendeten Browser geprüft werden.
  - Unabhängiger Wiederherstellungsschlüssel (`KF-XXXX-XXXX-...`) für portable Backups und Neuinstallationen.
  - Automatische Sperre bei Inaktivität und manueller Sofort-Sperrknopf.

---

## Schnellstart

### 1. Starten

Doppelklick auf `Klausurfix.vbs` startet den lokalen Hintergrunddienst ohne sichtbares Konsolenfenster und öffnet die Anwendung in Microsoft Edge. Wenn Edge nicht gefunden wird, öffnet sich der Standardbrowser. Ein weiterer Start verwendet die bereits laufende Instanz.

Für eine Desktop-Verknüpfung einmalig `Verknuepfung-erstellen.vbs` doppelklicken. Die Verknüpfung verweist auf den aktuellen Programmordner; nach einem Verschieben des Ordners muss sie neu erstellt werden. Eine bereits vorhandene Verknüpfung gleichen Namens wird nicht überschrieben.

Für die Ausführung aus dem Quellordner wird das [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) benötigt. Auf einem Surface mit Snapdragon-Prozessor sollte die ARM64-Version installiert werden. Beim ersten Start und nach dem Wechsel von .NET 9 werden Projektabhängigkeiten eingerichtet; dafür kann eine Internetverbindung erforderlich sein. Nach erfolgreicher Einrichtung ist der Betrieb offline möglich. Startfehler werden in einem Dialog angezeigt.

Mit **Beenden** in der Anwendung werden offene Eingaben gespeichert und der lokale Hintergrunddienst beendet. Anschließend kann der Browser-Tab geschlossen werden.

Für die Entwicklung alternativ im Terminal:
```bash
dotnet run --project src/Klausurfix.Web/Klausurfix.Web.csproj
```
Klausurfix bindet ausschließlich an den lokalen Loopback-Adapter und wird im Browser unter `http://localhost:5000` geöffnet.

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
├── Klausurfix.vbs                     # Unsichtbarer Start für den Windows-Desktop
├── Verknuepfung-erstellen.vbs         # Erstellt eine Desktop-Verknüpfung
├── scripts/start-klausurfix.ps1       # Wartet auf den lokalen Dienst
├── start-klausurfix.cmd               # Kompatibler Start über die Konsole
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
    └── Klausurfix.Tests/             # Automatische xUnit Unit- & Integrationstests
```

---

## Tests ausführen

Alle fachlichen Berechnungen, Sicherheitsfunktionen, Datenbank-Rundläufe und Exportfunktionen sind durch automatisierte Tests abgedeckt:

```bash
dotnet test
```

