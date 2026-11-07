# Entwicklung der Offline-Updates

Dieser Ordner enthält einen Update-Assistenten in Entwicklung, **noch kein produktionsreifes Update**. Der unterstützte Ablauf setzt einen geeigneten Versionsstarter voraus; für Version 0.1.4 fehlt weiterhin ein automatischer Übergang.

## Paketprüfung

`Paketpruefung` prüft Struktur, Dateigrößen und Prüfsummen eines ZIP-Pakets. Es entpackt oder installiert nichts. Aufruf aus dem Repository:

```powershell
dotnet run --project update-entwicklung/Paketpruefung -- paket.zip
```

Das ZIP enthält `manifest.json` und ausschließlich die im Manifest aufgeführten Dateien unter `app/`. Das Manifest verwendet beispielsweise folgende Struktur (Prüfsumme hier nur schematisch):

```json
{"Format":1,"Produkt":"Klausurfix","Version":"0.2.0","Architektur":"win-arm64","Dateien":[{"Pfad":"app/Klausurfix.exe","Groesse":123,"Sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}]}
```

Auf Nutzerwunsch werden Paket und EXE nicht signiert. Die Prüfsummen erkennen Beschädigungen, belegen aber nicht die Herkunft und verhindern keine gezielte Veränderung von Paket und Manifest.

## Paketbau

Mit einem fertigen Programmordner erzeugt der Paketbau ein ZIP. Er überschreibt keine vorhandene Ausgabedatei und lehnt Verknüpfungen im Eingabeordner ab:

```powershell
dotnet run --project update-entwicklung/Paketbau -- <programmordner> 0.2.0 win-arm64 <ausgabe.zip>
dotnet run --project update-entwicklung/Paketpruefung -- <ausgabe.zip>
```

Die Paketwerkzeuge selbst erstellen keine Update-EXE und verändern keine Installation.

## Prüf-Assistent als einzelne EXE

Im aktuellen Quellcode wird die ausgewählte Sicherung nach Eingabe des Wiederherstellungsschlüssels automatisch geprüft. Der Schlüssel kann auch aus der bei der Einrichtung angebotenen Textdatei gelesen werden. Die Installationsschaltfläche wird erst nach erfolgreicher Paket- und Sicherungsprüfung freigegeben; unmittelbar vor der Installation wird die Sicherung erneut geprüft. Der Installer registriert den Programmordner im Windows-Benutzerkonto; der Update-Assistent liest diese Registrierung. Ältere portable Versionen sind nicht registriert und müssen zunächst über den Installer umgestellt werden. Der vollständige Fensterablauf und der Surface-Test stehen aus.

Ab 0.2.1 fordert der Update-Assistent Administratorrechte an und prüft, dass das bestätigende Konto dem angemeldeten Desktop-Benutzer entspricht. Nach der Aktualisierung muss Klausurfix über die Verknüpfung im normalen Benutzerkonto gestartet werden; ein automatischer Neustart aus dem erhöhten Prozess erfolgt nicht. Die Rechteanfrage und der vollständige Aktualisierungsablauf sind noch nicht praktisch durchgehend erprobt.

Während der Installation zeigt der Update-Assistent eine laufende Fortschrittsanzeige ohne Prozentangabe. Nach erfolgreicher Aktivierung und Bestätigung der Erfolgsmeldung schließt sich der Assistent; bei Fehlern bleibt er geöffnet. Der Ablauf wurde noch nicht praktisch durchgehend geprüft.

Der Update-Assistent erlaubt auch die erneute Installation derselben Version ohne gesonderte Auswahl. Architektur, Sicherung, Dienststillstand und Paketprüfung gelten weiterhin. Ein bereits vorhandener Versionsordner wird nur wiederverwendet, wenn sämtliche Dateien exakt dem Paket entsprechen; veränderte Dateien werden nicht überschrieben. Eine Rückstufung auf eine ältere Version bleibt ausgeschlossen. Der tatsächliche Ablauf der einzelnen EXE ist noch nicht durchgehend erprobt.

Die korrigierte Update-EXE zu 0.2.1 bietet für ausdrücklich bestätigte Erstinstallationen ohne Daten zusätzlich einen Weg ohne Sicherung. Dabei prüft sie vor der Aktivierung die bekannten alten und neuen Datenordner sowie eine abweichend konfigurierte Datenablage und bricht bei vorhandenen Dateien oder Verknüpfungen ab. Dieser Sonderfall ist nur für eine noch nicht eingerichtete Installation gedacht; bei vorhandenen Daten bleibt die geprüfte Sicherung erforderlich. Der vollständige Ablauf dieses Sonderfalls wurde noch nicht praktisch getestet.

Der Update-Assistent ist eine **Entwicklungsfassung mit Installationsfunktion**. Die Nutzerin erstellt zuvor eine Sicherung und beendet Klausurfix. Der Assistent wählt die nach Dateierstellungszeit neueste `.kfbak` im neuen oder bisherigen verwalteten Sicherungsordner vor und zeigt Pfad und Zeitpunkt an. Die Auswahl kann geändert werden; externe Kopien werden nicht automatisch gesucht. Fehlt der Ordner oder eine Sicherung, ist eine manuelle Auswahl möglich. Die Vorauswahl prüft weder Inhalt noch Aktualität oder Zugehörigkeit; vor der Installation wird die ausgewählte Datei mit dem Wiederherstellungsschlüssel geprüft. Der Programmordner wird anhand der Registrierung des Installers gefunden. Der Assistent prüft den gestoppten Dienst und das Paket, ersetzt bei kompatibler Ausgangsversion auch den Starter, aktiviert die neue Version. Klausurfix wird danach selbst im normalen Benutzerkonto gestartet. Bei Unterbrechung der Umschaltung dieselbe Update-EXE erneut ausführen; der Assistent prüft die bereitgestellten Dateien erneut und setzt den Vorgang fort. Auf Nutzervorgabe wird die ausgewählte Sicherung als aktuell angenommen; ihre Zuordnung zum Datenbestand wird nicht geprüft. Ein echter End-to-End-Test der einzelnen EXE fehlt.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File update-entwicklung/erzeuge-pruef-assistent.ps1 -Paket <ausgabe.zip> -Architektur win-arm64 -Ausgabedatei <update-assistent.exe>
```

Der Build prüft das Paket vorab, bettet es ein und überschreibt keine vorhandene Ausgabedatei. Der Assistent ist noch nicht für den produktiven Einsatz bestimmt.

Für Version 0.1.9 wurden auf ausdrücklichen Wunsch nur die beiden einzelnen Update-EXE erzeugt. Mit `scripts/build-portable.ps1 -PackageVersion 0.1.9 -UpdateOnly` entstehen die Anwendung und das geprüfte Update-ZIP als temporäre Build-Zwischenprodukte; separate ZIP-Dateien bleiben nicht zurück. Beide Zwischenpakete wurden mit je 423 Dateien geprüft. Der tatsächliche Einzeldatei-Update-Ablauf und der Surface-Test stehen weiterhin aus.

Die Pakettests erzeugen nur kurzlebige synthetische Dateien im temporären Ordner:

```powershell
dotnet build update-entwicklung/Paketbau
dotnet build update-entwicklung/Paketpruefung
dotnet run --project update-entwicklung/Pakettests -- update-entwicklung/Paketbau/bin/Debug/net10.0/Paketbau.dll update-entwicklung/Paketpruefung/bin/Debug/net10.0/Paketpruefung.dll
```

## Bis zur fertigen Update-Funktion

Ziel ist eine einzelne offline verteilbare Update-EXE je unterstützter Architektur. Zuvor erstellt die Nutzerin eine verschlüsselte Sicherung und beendet Klausurfix selbst. Auf ihre ausdrückliche Vorgabe wird für den Update-Ablauf bei gestopptem Dienst angenommen, dass alle Eingaben gespeichert sind. Die EXE soll bei laufendem Dienst oder belegtem Port abbrechen und danach Installation und Neustart begleiten. Die nachstehenden Punkte sind offen; die später beschriebenen Bausteine erfüllen sie noch nicht als zusammenhängenden Ablauf.

1. **Vorbereitung in Klausurfix:** Die Nutzerin beendet die entsperrte Anwendung selbst. Dabei wird nach der Speicherabstimmung eine verschlüsselte Sicherung erstellt; offene Formulare müssen zuvor gespeichert oder geschlossen werden.
2. **Stillstand prüfen:** Der Assistent prüft den Dienst und Port 5000 und bricht bei laufendem Dienst oder belegtem Port ab. Diese Prüfung muss während der Installation wiederholt und mit Starter und Update-Prozess gegen parallele Starts koordiniert werden.
3. **Sicherung prüfen:** Die vor dem Beenden erstellte Sicherung vor der Installation auf Integrität prüfen. Auf Nutzervorgabe gilt die ausgewählte Sicherung als aktuell; eine Zuordnung zum Datenbestand erfolgt nicht. Bei fehlgeschlagener Prüfung nicht installieren. Sicherung und unabhängig verwahrten Wiederherstellungsschlüssel erhalten.
4. **Manuelles Beenden:** Der Assistent beendet die App nicht. Die Nutzerin schließt Klausurfix über „Beenden“ und startet anschließend den Assistenten erneut.
5. **Installieren und neu starten:** Die vorhandene Paketprüfung, Versionsablage und Umschaltung mit dem Assistenten verbinden. Architektur, Ausgangsversion, Paket und Speicherplatz vor Änderungen prüfen; nur vollständig geprüfte Dateien in einem separaten Versionsordner bereitstellen. Sichere Pfade, Unterbrechungen, belegten Port und Fehler bei der Umschaltung behandeln. Eine bestehende Verknüpfung nicht ungefragt überschreiben. Entsperrung und gegebenenfalls Migration sichtbar begleiten; nach inkompatibler Datenmigration keine automatische Rückkehr zu einer älteren Version.
6. **Übergang von 0.1.4:** Einen gesonderten Ablauf für bestehende Installationen und deren Verknüpfungen entwickeln und testen. Der damalige Starter versteht den neuen Versionsverweis nicht und die Oberfläche bietet den neuen Abstimmungsweg nicht.
7. **Verteilung:** Die Update-EXE wird auf Nutzerwunsch ohne Herausgebersignatur bereitgestellt. Ihre Herkunft lässt sich durch die Paketprüfung nicht feststellen.
8. **End-to-End-Prüfung:** Den tatsächlich gebauten Einzeldatei-Assistenten mit synthetischen Daten und temporären Verzeichnissen prüfen: Neustarts, Abbrüche, Sperren, fehlerhafte Sicherungen, beschädigte Pakete, Pfadumgehung, belegten Port, parallele Starts, Versionswechsel, Migration und Wiederherstellung. Architektur und Windows-Version des Zielgeräts vor der Verteilung feststellen. Den vollständigen Offline-Ablauf, die Oberfläche und Windows Hello auf dem Surface testen; bis dahin als ungeprüft ausweisen.

Die updatebezogenen Tests für die vorhandenen Bausteine bestanden am 7. November 2026 lokal mit 17 erfolgreichen Tests. Sie belegen weder den vollständigen Ablauf noch einen Surface- oder Offline-Gerätetest.

Die Paketprüfung ist eine Entwicklungsgrundlage und ersetzt keine Sicherheitsprüfung oder getestete Installationsroutine.

## Vorbereitete Sicherungsschnittstelle

Aktuelle Entwicklungsstände stellen `POST /api/update/backup` für eine regulär authentifizierte lokale Browser-Sitzung bereit. Die Schnittstelle verwendet den bestehenden Sicherungsdienst, legt eine verwaltete Sicherung mit Präfix `klausurfix_update_` an und liefert deren Metadaten zurück. Sie benötigt keine Übermittlung des Wiederherstellungsschlüssels; dieser bleibt zum Wiederherstellen erforderlich. Ohne gültige Sitzung wird die Anfrage abgewiesen. Die bestehenden Prüfungen auf Host, Origin und `X-Requested-With` gelten unverändert.

Die Schnittstelle sichert ausschließlich bereits im Backend gespeicherte Daten. Sie speichert keine offenen Browser-Eingaben, reserviert den Datenbestand nicht gegen spätere Änderungen und beendet den Dienst nicht. Die Sicherungsschnittstelle allein ist keine Freigabe für eine Installation.

## Speicheraufforderung in der App

Ab 0.2.2 wartet der anfordernde Tab nach erfolgreicher Antwort des Beenden-Endpunkts nicht mehr auf eine weitere Abbruchanfrage an den herunterfahrenden Dienst. Er entfernt die Eingabesperre und zeigt den beendeten Zustand. Gleichursprüngliche Tabs im selben Browserprofil werden über BroadcastChannel benachrichtigt; in anderen Profilen ist dies nicht garantiert. Bei einem nicht bestätigten Beenden wird keine Erfolgsmeldung angenommen; die Abbruchanfrage ist zeitlich begrenzt und die lokale Eingabesperre wird aufgehoben. Eine erfolgreiche Sicherung und die serverseitigen Stillstandsprüfungen bleiben weiterhin erforderlich.

Beim Beenden fordert die Anwendung die beim Backend registrierten Ansichten zur Speicherung auf, auch über Browserprofile und Origins hinweg. Während der Speicherabstimmung werden Eingaben in diesen Ansichten vorübergehend gesperrt. Klausurtabelle und Deckblatt verwenden ihre vorhandenen Speicherwege. Offene Dialogformulare sowie die Ansichten zum Anlegen einer Klausur und für Einstellungen müssen zunächst gespeichert oder verlassen werden; sie werden nicht automatisch abgeschickt. Wenn eine registrierte Ansicht nicht antwortet oder ein Speichern fehlschlägt, bleibt die Anwendung geöffnet. Nach den Speicherbestätigungen wird eine verschlüsselte Sicherung angelegt und die Abstimmung erneut geprüft; erst danach wird der Dienst beendet. Es ist nicht nachgewiesen, dass alle offenen Ansichten registriert sind.

`UpdateViewRegistry` verwaltet die registrierten Ansichten und die Speicher-Runde im Arbeitsspeicher. Sie erwartet Antworten aller registrierten Ansichten und verwirft eine Runde, wenn sich die Menge der Ansichten ändert oder eine Speicherung fehlschlägt. Die Runde läuft nach 30 Sekunden ab. Eine sichere Behandlung von Seiten-Neustart, Browserabsturz und Dienstneustart fehlt. Die Registrierung ist kein Nachweis, dass alle Ansichten erfasst sind, und keine Freigabe für Sicherung oder Installation.

Seit 7. November 2026 gilt zusätzlich: Antwortet eine registrierte Ansicht zehn Sekunden lang nicht auf die regelmäßige Abfrage, kann keine neue Speicherabstimmung beginnen; eine laufende Runde wird ungültig, auch wenn die Ansicht zuvor bereits gespeichert gemeldet hat. Die Abfrage läuft während des Speicherns weiter. Die Ansicht wird nicht automatisch entfernt. Bei einem Browserabsturz kann ihre Registrierung daher bis zum Dienstneustart bestehen bleiben. Diese Prüfung erkennt keine unregistrierten Ansichten und übersteht keinen Dienstneustart; sie ist weiterhin keine Installationsfreigabe.

## Vorbereitete Backend-Schreibsperre

`UpdateWriteGate` serialisiert schreibende HTTP-API-Aufrufe. Eine interne Sperre wartet, bis ein laufender Aufruf beendet ist, und weist weitere schreibende Aufrufe mit HTTP 423 ab, bis die Sperre freigegeben wird. Lesende Aufrufe und die unten genannten Koordinationsanfragen bleiben möglich. Die Sperre erfasst keine Hintergrundarbeiten außerhalb der HTTP-Pipeline. Ein späterer Installationsablauf muss die Sperre bei Fehler und Abbruch zuverlässig freigeben und Sicherung, Sperrung, Ablaufzeit, laufende Hintergrundarbeiten und das Beenden gemeinsam abstimmen. Weder eine gehaltene Sperre noch die Sicherungsschnittstelle allein sind eine Installationsfreigabe.

`UpdatePreparation` verbindet intern diese Sperre mit der Erstellung einer verschlüsselten verwalteten Update-Sicherung. Es wartet zuerst auf laufende HTTP-Schreibvorgänge, hält die Sperre während der Sicherung und gibt sie bei Fehler oder Abbruch wieder frei. Bei Erfolg bleibt sie bis zur ausdrücklichen Freigabe durch den aufrufenden Ablauf bestehen. Ein Abbruch während der synchronen Sicherung wird nach deren Abschluss erkannt; eine dabei entstandene Sicherung bleibt erhalten. Auch dieser interne Baustein prüft keine offenen Browseransichten und beendet die Anwendung nicht. Es gibt noch keine Schnittstelle für den Update-Assistenten.

`POST /api/application/shutdown` wartet nach einer abgeschlossenen Speicherabstimmung auf laufende HTTP-Schreibvorgänge und Sicherungsläufe, hält die Schreibsperre und prüft die Abstimmung vor und nach der Sicherung. Erst nach erfolgreicher Sicherung wird der Dienst beendet. Eine bei Abbruch bereits erstellte Sicherung bleibt erhalten. Andere mögliche Hintergrundarbeiten außerhalb der HTTP-Pipeline sind von dieser Sperre nicht allgemein erfasst. Der Endpunkt erfordert eine authentifizierte Sitzung; die Abstimmung ist keine Freigabe zur Installation.

## Vorbereitete Versionsablage

`Versionsablage/VersionStager` prüft ein Paket und stellt die Programmdateien unter einem neuen Ordner `versionen/<version>-<architektur>` bereit. Architektur, Starter, Dienst und Dateihashes werden geprüft. Die Dateien werden zunächst in einem eigenen Vorbereitungsordner geschrieben und erst nach vollständigem Schreiben in den Versionsordner umbenannt; eine bestehende Version wird nicht überschrieben. Bei einem Fehler kann ein `.vorbereitung-...`-Ordner zurückbleiben. Er wird nicht automatisch gelöscht. Wiederaufnahme nach Unterbrechung und Prüfungen auf dem Zielgerät stehen aus.

Der Starter kann zusätzlich den Dienst anhand einer lokalen Datei `aktive-version.txt` aus einem Versionsordner starten. `StoppedVersionInstaller` stellt das Paket bereit, ersetzt den Starter und schaltet die aktive Version um. Ein journalisierter Austausch bewahrt den bisherigen Starter vorübergehend auf; bei Unterbrechung verweigert der neue Starter den Start, bis dieselbe Update-EXE den Vorgang fortsetzt. Der Ablauf verwendet den Starter-Mutex, prüft Architektur, Dienst und Port und lehnt fehlende Dateien sowie eine automatische Rückstufung ab. Er setzt eine Ausgangsversion mit `update-starter-v1.txt` und Basiskennung voraus. Ältere Installationen wie 0.1.4 können damit nicht automatisch umgestellt werden. Der Baustein beendet oder sichert die Anwendung nicht und garantiert nicht, dass außerhalb des Starter-Mutex kein Dienst gestartet wird.

Der Assistent übergibt das eingebettete Paket als Stream und ruft nach der Umschaltung den vorhandenen Starter auf. Der Starter-Mutex bleibt bis zum Startaufruf gehalten. Wenn der Startaufruf fehlschlägt, bleibt die neue Version aktiviert; ein automatisches Zurückschalten findet nicht statt. Die Verbindung ist bisher nur auf Bausteinebene geprüft; der tatsächliche Einzeldatei-Test steht aus.
