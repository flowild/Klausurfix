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
- **Aufgabenpunkte:** Neue Höchstpunkte und erreichte Punkte werden ganzzahlig eingegeben. Die Höchstpunkte neuer Klausuren ergeben zusammen genau 100; bestehende Klausuren bleiben unverändert. Bereits gespeicherte dezimale Werte bleiben erhalten; berechnete prozentuale Abzüge können dezimal sein.
- **Flexible Schulformen & Bewertungsprofile:**
  - Wahlweise **Schulnoten (1–6)** oder **Oberstufen-Notenpunkte (0–15)**.
  - Frei definierbare Fehlerquotient-Abzugstabelle: Stufen ab Grenze einschließlich bis zur nächsten Grenze ausschließlich.
  - Prozentualer Notenschlüssel mit exakter Grenzprüfung ohne Vorab-Rundung.
- **Klausur-Snapshotting:** Jede Klausur friert ihr Regelwerk und ihre Schülerliste bei Erstellung ein. Spätere Stammdatenänderungen verändern historische Klausuren niemals stillschweigend.
- **Klausurarchiv:** Klausuren können aus der Übersicht archiviert und im separaten Archiv geöffnet oder wiederhergestellt werden. Im Archiv sind Klausuren samt Deckblatt schreibgeschützt; nach dem Wiederherstellen lassen sie sich wieder bearbeiten. Archivierte Klausuren bleiben mit ihren Ergebnissen und Deckblättern Bestandteil der verschlüsselten Sicherungen.
- **Saubere Status-Trennung:** Regulär bewertete Schüler, tatsächlich 0 Punkte, unvollständige und krankheitsbedingt fehlende Schüler werden exakt unterschieden; Abwesende werden im Klassenschnitt nicht als 0 gewertet.
- **Automatische Sicherung:** In den Einstellungen ein- oder ausschalten und das Intervall auf 6 Stunden, 12 Stunden, täglich oder wöchentlich einstellen; voreingestellt ist täglich. Fällige Sicherungen werden bei laufender und entsperrter Anwendung erstellt. Ist die App geschlossen oder gesperrt, erfolgt die fällige Sicherung nach dem nächsten Entsperren. In den Einstellungen sind letzte Sicherung und Fehler sichtbar. Automatische Sicherungen bleiben bis zur manuellen Löschung erhalten und können mit dem Wiederherstellungsschlüssel wiederhergestellt werden.
- **PDF- & DOCX-Export:** Übersichtsberichte im Querformat mit Aufgabenmatrix, waagerechtem Notenspiegel und Bewertungsregeln.
- **Klausurendeckblätter:** Bearbeitbare Aufgabenstellungen, Klausurdaten, Höchstpunkte, Notenschlüssel und Hinweise im A4-Hochformat. Pro Klausur auswählbare Operatoren stehen mit Definition auf der ersten Seite; PDF und DOCX sind offline verfügbar.
- **Sicherheit & Windows Hello:**
  - App-PIN mit 6 bis 64 Ziffern, PBKDF2-Schlüsselableitung (600.000 Iterationen) und progressiver Verzögerung bei Fehlversuchen. Kurze PINs bleiben für eine einfache Bedienung erlaubt.
  - Die PIN-Schlüsselhülle wird zusätzlich an den Windows-Benutzer gebunden. Die Geräteanmeldung über WebAuthn erfordert Benutzerverifikation und einen PRF-fähigen Plattformauthentifikator; die App-PIN bleibt verfügbar. Die Funktion muss auf dem Surface mit Windows Hello und dem verwendeten Browser geprüft werden.
  - Unabhängiger Wiederherstellungsschlüssel (`KF-XXXX-XXXX-...`) für portable Backups und Neuinstallationen.
  - Automatische Sperre bei Inaktivität und manueller Sofort-Sperrknopf.

---

Verschlüsselte Datenbankfelder sind über zusätzliche authentifizierte Daten (AAD) an Formatversion, Tabelle, Spalte und vollständigen Primärschlüssel gebunden. Bei Klausureinträgen gehören sowohl Klausur-ID als auch Schüler-ID dazu. Kopierte Chiffretexte aus anderen Feldern oder Datensätzen werden abgewiesen. Grundlage ist die [AAD-Unterstützung von AES-GCM in .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt). Beim ersten Entsperren mit dieser Version werden ältere Felder innerhalb einer SQLite-Transaktion in das Format `kf2:` migriert; fachliche Snapshots, Archivstatus und Deckblätter bleiben erhalten. Nach der Umstellung akzeptieren reguläre Lesezugriffe keine alten Feldformate oder Klartexte. Anschließend werden freie Datenbankseiten und das WAL bereinigt; eine unterbrochene Bereinigung wird beim nächsten Entsperren fortgesetzt. Die umgestellte Arbeitsdatenbank kann mit älteren App-Versionen nicht mehr gelesen werden. Das Sicherungsformat bleibt kompatibel; beim Wiederherstellen werden Felder mit der neuen Bindung gespeichert.

Der Schutz hat Grenzen: SQLite-Struktur, IDs, Beziehungen, Datensatzzahlen, Sortierreihenfolge und Archivkennzeichen bleiben sichtbar und sind nicht durch die Feldbindung geschützt. Die Bindung verhindert keine Löschungen und kein Zurückspielen älterer Daten am ursprünglichen Speicherplatz. Bereits vor der Migration vertauschte alte Felder lassen sich nachträglich nicht sicher erkennen. DPAPI schützt den PIN-Pfad durch Bindung an den Windows-Benutzer; Programme im passenden Benutzerkonto können diese Schicht öffnen und PINs offline prüfen. Die App-Verzögerungen gelten nur für Anmeldeversuche über die App. Kurze oder leicht erratbare PINs bieten deshalb begrenzten Schutz gegen diesen Angriff. Klausurfix ersetzt keinen Windows-Geräteschutz und schützt nicht zuverlässig vor Schadsoftware im bereits entsperrten Benutzerkonto.

## Schnellstart

### 1. Starten

Für die portable Version 0.1.1 das passende ZIP vollständig entpacken und `Klausurfix.vbs` starten: `win-arm64` für Windows auf ARM (z. B. Surface Pro 11), `win-x64` für Windows auf Intel-/AMD-Prozessoren. Die Laufzeit ist enthalten; es wird kein .NET SDK benötigt. Arbeitsdaten und verwaltete Sicherungen liegen weiterhin im lokalen Windows-Benutzerprofil unter `%LOCALAPPDATA%\Klausurfix` und werden nicht mit dem Programmordner verschickt. Die ARM64-Version und Windows Hello müssen auf dem Surface noch geprüft werden.

Zum Aktualisieren von 0.1 zunächst dort eine verschlüsselte Sicherung erstellen und die App mit **Beenden** schließen. Version 0.1.1 in einen eigenen Programmordner entpacken und daraus starten; vorhandene Arbeitsdaten werden beim Entsperren migriert. Die umgestellte Arbeitsdatenbank ist mit 0.1 nicht mehr lesbar. Eine bisherige Desktop-Verknüpfung verweist weiterhin auf den alten Programmordner und muss angepasst werden.

Die Pakete lassen sich mit `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-portable.ps1` aus dem Quellcode erstellen; sie erscheinen unter `release/`. Die Lizenzbedingungen der mitgelieferten Komponenten liegen im Paket unter `Lizenzen`. Für QuestPDF ist im Code die Community-Lizenz eingestellt; deren Voraussetzungen sind für den jeweiligen Einsatz zu prüfen.

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
4. Nach Bestätigung startet Klausurfix direkt in die Klausurenübersicht. Ein Klick auf eine Klausur öffnet ihre Eingabetabelle.

---

## Klausurendeckblatt erstellen

1. Nach dem Anlegen einer Klausur **Deckblatt erstellen** wählen oder das Angebot überspringen. Das Deckblatt kann auch später in der Eingabetabelle oder Auswertung über **Deckblatt erstellen** geöffnet werden.
2. Der Klausurtitel erscheint automatisch mit „Klausur“ in der Kopfzeile. Bei Bedarf einen Zusatz und weitere Hinweise ergänzen und für jede bestehende Aufgabe die Aufgabenstellung eingeben. Aufgabenanzahl und Höchstpunkte werden aus der Klausur übernommen.
3. Bis zu sechs Operatoren für die erste Seite manuell auswählen oder mit **Operatoren aus Aufgaben übernehmen** erkennen und auswählen lassen. Dieser Knopf ersetzt die aktuelle Auswahl; sie kann danach angepasst werden. Bei mehr als sechs erkannten Operatoren werden die ersten sechs übernommen. Die Suche durchsucht Namen und Definitionen. Zunächst sind „wiedergeben“, „erklären“ und „diskutieren“ aus der Beispielvorlage ausgewählt; sie können abgewählt werden. Erkannte Operatoren werden in den Aufgaben automatisch unterstrichen. Die Erkennung kann je nach Formulierung unvollständig oder mehrdeutig sein; bitte Unterstreichung und Auswahl prüfen.
4. Das Logo der Saalburgschule aus der Vorlage, den Notenschlüssel, den Abzugshinweis und das Feld zur Wortanzahl nach Bedarf ein- oder ausblenden. Der Abzugshinweis kann bearbeitet oder auf den Standardtext zurückgesetzt werden; die automatische Berechnung berücksichtigt Rechtschreibfehler.
5. Den Status **Gespeichert** abwarten und **PDF herunterladen** oder **DOCX herunterladen** wählen. Das DOCX enthält nach dem Deckblatt eine leere Seite mit Kopfbereich zum Einfügen des Quelltexts. Eingaben werden automatisch lokal gespeichert; vor dem Export und Schließen werden ausstehende Änderungen gespeichert. Bei einem Speicherfehler bleibt der Editor offen und versucht das Speichern automatisch erneut.

Die Gestaltung orientiert sich an der Vorlage: mittiges Schullogo, Namensfeld rechts, graue Tabellenkopfzeile, zentrierte Hinweise und links angeordnete Aufgaben mit eingerücktem Text und Punktfeld rechts. Der Abzugshinweis wird sofort in der Vorschau aktualisiert; der Speicherstatus zeigt noch nicht gespeicherte Änderungen an. Das genaue Drucklayout und Seitenumbrüche im heruntergeladenen Dokument prüfen. Bei langen Aufgaben kann das Dokument mehrere Seiten umfassen. Operatoren stehen nur auf der ersten Seite, Seitenzahlen auf jeder Seite. Die Materialseite der Beispielklausur wird nicht angehängt. Das Deckblatt enthält ein leeres Namensfeld.

Als Layoutvorlage dient die bereitgestellte Datei `11 BG PoWi Klausur 08.05.2025.docx`. Deren Tabelle überschneidet sich bei 20 Punkten; außerdem unterscheidet sich das Datum im Dokument vom Dateinamen. Daher verwendet die Funktion das Datum und den gespeicherten Notenschlüssel der jeweiligen Klausur. Die angezeigten Bereiche enthalten nur die jeweils zugehörigen ganzzahligen Prozentwerte; ein Strich bedeutet, dass kein ganzzahliger Wert in der Stufe liegt. Berechnet wird weiterhin mit den exakten Grenzen. Die Funktion übernimmt keine neue amtliche Bewertungstabelle und verändert keine bestehenden Klausurprofile.

Die 31 Operatoreneinträge einschließlich zusammengefasster Synonyme stammen aus `la27-operatoren-deutsch-musik-sport-fbii.pdf`: Hessisches Ministerium für Kultus, Bildung und Chancen, Landesabitur 2027, Stand **1. August 2025**. Insbesondere „erklären“ verwendet die Definition aus diesem PDF; die ältere Definition im Beispieldeckblatt wird nicht übernommen. Die dort angegebenen Anforderungsbereiche sind kontextabhängig. Die ausgewählten Definitionen und ihr Quellenstand werden je Klausur festgehalten.

Deckblattdaten werden authentifiziert verschlüsselt gespeichert und in vollständige Sicherungen aufgenommen. Neue Sicherungen verwenden Datenversion 4 und enthalten den Archivstatus sowie Deckblatthinweise und eingetragene Punkte; Sicherungen der Datenversionen 1 bis 3 können weiterhin eingelesen werden. Für die Wiederherstellung neuer Sicherungen ist diese oder eine neuere Klausurfix-Version erforderlich. Originaldateien vom Desktop und Materialabbildungen werden nicht in Arbeitsdaten oder Repository kopiert; lediglich das Vorlagenlogo wird als lokale Programmressource mitgeliefert.

Automatische und neu erstellte manuelle Sicherungen verwenden Sicherungsformat 2: Aus dem zufälligen Datenschlüssel wird mit HKDF-SHA-256 und einem neuen Salt ein gesonderter Sicherungsschlüssel abgeleitet. Die Sicherung enthält zusätzlich den mit dem Wiederherstellungsschlüssel geschützten Datenschlüssel; das Wiederherstellungsgeheimnis selbst wird nicht gespeichert. Für das Erstellen und Herunterladen genügt die entsperrte Anwendung; zur Wiederherstellung ist der ursprüngliche Wiederherstellungsschlüssel nötig. Bei einer Neuinstallation kann die Datei damit geöffnet und unter dem neuen lokalen Datenschlüssel wiederhergestellt werden. Ältere App-Versionen können Format 2 nicht wiederherstellen. Frühere Sicherungen im Format 1 bleiben wiederherstellbar. Es wird ein konsistenter SQLite-Snapshot im Arbeitsspeicher erstellt und die verwaltete Datei vor atomarer Übernahme geprüft.

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

