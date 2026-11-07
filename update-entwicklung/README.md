# Entwicklung der Offline-Updates

Dieser Ordner enthält Entwicklungsgrundlagen, **noch kein installierbares Update**. Die bestehende Version 0.1.4 besitzt keine Update-Steuerung; ihre Browser-Sitzung und gegebenenfalls noch nicht gespeicherte Eingaben dürfen nicht umgangen werden.

## Paketprüfung

`Paketpruefung` prüft ein ZIP mit einem extern bereitgestellten, vertrauenswürdigen öffentlichen RSA-Schlüssel. Es entpackt oder installiert nichts. Aufruf aus dem Repository:

```powershell
dotnet run --project update-entwicklung/Paketpruefung -- paket.zip vertrauenswuerdiger-schluessel.pem
```

Das ZIP enthält `manifest.json`, `manifest.sig` und ausschließlich die im Manifest aufgeführten Dateien unter `app/`. `manifest.sig` ist die binäre RSA-PSS-SHA-256-Signatur der unveränderten UTF-8-Bytes von `manifest.json`; der Schlüssel muss mindestens 3072 Bit haben. Das Manifest verwendet beispielsweise folgende Struktur (Prüfsumme hier nur schematisch):

```json
{"Format":1,"Produkt":"Klausurfix","Version":"0.2.0","Architektur":"win-arm64","Dateien":[{"Pfad":"app/Klausurfix.exe","Groesse":123,"Sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}]}
```

Der Schlüssel darf nicht aus dem zu prüfenden Paket stammen. Diese Prüfung belegt nur die Herkunft des Paketinhalts bei vertrauenswürdigem Schlüssel; sie prüft nicht die Authentizität einer ausführbaren Update-Datei. Für die Auslieferung müssen Schlüsselverwaltung, geprüfte Einbettung des öffentlichen Schlüssels, Signatur der Update-EXE, Schlüsseltausch und Offline-Prüfung festgelegt werden. Keine privaten Schlüssel im Repository ablegen.

## Paketbau

Mit einem extern verwahrten privaten RSA-Schlüssel und einem fertigen Programmordner erzeugt der Paketbau ein signiertes ZIP. Er überschreibt keine vorhandene Ausgabedatei und lehnt Verknüpfungen im Eingabeordner ab:

```powershell
dotnet run --project update-entwicklung/Paketbau -- <programmordner> 0.2.0 win-arm64 <privater-schluessel.pem> <ausgabe.zip>
dotnet run --project update-entwicklung/Paketpruefung -- <ausgabe.zip> <oeffentlicher-schluessel.pem>
```

Die Paketwerkzeuge erstellen weiterhin keine Update-EXE und verändern keine Installation. Schlüssel müssen außerhalb des Repositorys angelegt und geschützt aufbewahrt werden.

## Prüf-Assistent als einzelne EXE

Der Update-Assistent ist bislang ein **Prüfprototyp**. Er enthält ein zuvor signiertes Paket und einen beim Bau festgelegten öffentlichen Schlüssel, prüft Inhalt und Windows-Architektur und zeigt das Ergebnis in einem Fenster. Er installiert nichts und kann Klausurfix noch nicht sichern oder beenden. Die EXE selbst muss vor einer späteren Verteilung unabhängig vertrauenswürdig signiert und geprüft werden; eine Prüfung durch die EXE selbst reicht dafür nicht aus.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File update-entwicklung/erzeuge-pruef-assistent.ps1 -Paket <ausgabe.zip> -OeffentlicherSchluessel <oeffentlicher-schluessel.pem> -Architektur win-arm64 -Ausgabedatei <pruef-assistent.exe>
```

Der Build prüft das Paket vorab, bettet es zusammen mit dem extern bereitgestellten öffentlichen Schlüssel ein und überschreibt keine vorhandene Ausgabedatei. Der Assistent ist noch nicht für den produktiven Einsatz bestimmt.

## Unabhängige Prüfung einer signierten EXE

Das separate Entwicklungswerkzeug `Dateisignaturpruefung` prüft eine eingebettete Authenticode-Signatur mit Windows `WinVerifyTrust` und vergleicht anschließend den SHA-256-Fingerabdruck des Signaturzertifikats. Es startet die geprüfte Datei nicht. Der erwartete Fingerabdruck muss vorher unabhängig und vertrauenswürdig festgelegt werden; ein mit der Update-Datei gelieferter Fingerabdruck genügt nicht.

```powershell
dotnet run --project update-entwicklung/Dateisignaturpruefung -- <update.exe> <zertifikat-sha256>
powershell -NoProfile -ExecutionPolicy Bypass -File update-entwicklung/test-dateisignatur.ps1
```

Die Windows-Prüfung ist auf Cache-Betrieb ohne Netzwerkabrufe eingestellt. Eine aktuelle Sperrprüfung für kompromittierte Zertifikate ist offline nicht gewährleistet; die Vertrauens- und Zeitstempelkette hängt vom lokalen Windows-Zertifikatsspeicher ab. Das Werkzeug ist ein Entwicklungsbaustein: Die unabhängige sichere Verteilung des Prüfprogramms und des Fingerabdrucks, der Wechsel kompromittierter oder ablaufender Zertifikate, weitere Signaturvarianten sowie die sichere Übergabe der geprüften Datei an einen späteren Installationsprozess sind noch nicht umgesetzt. Es ist keine Freigabe für den produktiven Update-Prozess.

Die Pakettests erzeugen nur kurzlebige synthetische Dateien und Testschlüssel im temporären Ordner:

```powershell
dotnet build update-entwicklung/Paketbau
dotnet build update-entwicklung/Paketpruefung
dotnet run --project update-entwicklung/Pakettests -- update-entwicklung/Paketbau/bin/Debug/net10.0/Paketbau.dll update-entwicklung/Paketpruefung/bin/Debug/net10.0/Paketpruefung.dll
```

## Bis zur fertigen Update-Funktion

Ziel ist eine einzelne offline verteilbare Update-EXE je unterstützter Architektur. Sie begleitet sichtbar das Speichern, eine verschlüsselte und geprüfte Sicherung, das geordnete Beenden, die Installation und den Neustart. Die nachstehenden Punkte sind offen; die später beschriebenen Bausteine erfüllen sie noch nicht als zusammenhängenden Ablauf.

1. **Eingaben abstimmen:** Eine geschützte Verbindung zwischen Assistent und laufender App herstellen. Alle relevanten Tabs, Fenster und Browserprofile müssen zuverlässig erfasst werden und ihre Eingaben speichern oder den Vorgang stoppen. Ungültige Eingaben, nicht registrierte oder eingefrorene Ansichten sowie Seiten-, Browser- und Dienstneustarts sicher behandeln. Die bestehende Speicheraufforderung ist keine Installationsfreigabe.
2. **Änderungen verhindern:** Nach der Speicherabstimmung laufende Schreibvorgänge und Hintergrundarbeiten koordinieren und bis zum geordneten Beenden weitere Änderungen verhindern. Die Sperre bei Fehlern und Abbruch zuverlässig freigeben; bestehende Sitzungs-, Host-, Origin- und CSRF-Prüfungen beibehalten.
3. **Sicherung anschließen:** Eine vollständige verwaltete verschlüsselte Sicherung erstellen und ihre Integrität prüfen, bevor die Installation beginnt. Bei unklarer Speicherlage oder fehlgeschlagener Sicherung weder installieren noch den Dienst zwangsweise beenden. Sicherung und unabhängig verwahrten Wiederherstellungsschlüssel erhalten.
4. **Geordnet beenden:** Den Dienst kontrolliert stoppen und andere laufende Instanzen erkennen. Starter, Dienst und Update-Prozess gegen parallele Starts und Aktualisierungen koordinieren.
5. **Installieren und neu starten:** Die vorhandene Paketprüfung, Versionsablage und Umschaltung mit dem Assistenten verbinden. Architektur, Ausgangsversion, Paket und Speicherplatz vor Änderungen prüfen; nur vollständig geprüfte Dateien in einem separaten Versionsordner bereitstellen. Sichere Pfade, Unterbrechungen, belegten Port und Fehler bei der Umschaltung behandeln. Eine bestehende Verknüpfung nicht ungefragt überschreiben. Entsperrung und gegebenenfalls Migration sichtbar begleiten; nach inkompatibler Datenmigration keine automatische Rückkehr zu einer älteren Version.
6. **Übergang von 0.1.4:** Einen gesonderten Ablauf für bestehende Installationen und deren Verknüpfungen entwickeln und testen. Der damalige Starter versteht den neuen Versionsverweis nicht und die Oberfläche bietet den neuen Abstimmungsweg nicht.
7. **Herausgebervertrauen und Verteilung:** Eine unabhängig vertrauenswürdige Herausgeberidentität, Signierung und Prüfung der Update-EXE vor Ausführung, sichere Verteilung sowie Schlüssel- und Zertifikatswechsel festlegen und umsetzen. Ein nur im Update mitgelieferter Schlüssel oder Fingerabdruck authentifiziert die EXE nicht. Die Grenzen der Offline-Zertifikatsprüfung dokumentieren; private Schlüssel bleiben außerhalb des Repositorys.
8. **End-to-End-Prüfung:** Den tatsächlich gebauten Einzeldatei-Assistenten mit synthetischen Daten und temporären Verzeichnissen prüfen: mehrere Tabs und Browser, ungültige oder ungespeicherte Eingaben, Neustarts, Abbrüche, Sperren, fehlerhafte Sicherungen, manipulierte oder falsch signierte Pakete, Pfadumgehung, belegten Port, parallele Starts, Versionswechsel, Migration und Wiederherstellung. Architektur und Windows-Version des Zielgeräts vor der Verteilung feststellen. Den vollständigen Offline-Ablauf, die Oberfläche und Windows Hello auf dem Surface testen; bis dahin als ungeprüft ausweisen.

Die updatebezogenen Tests für die vorhandenen Bausteine bestanden am 7. November 2026 lokal mit 17 erfolgreichen Tests. Sie belegen weder den vollständigen Ablauf noch einen Surface- oder Offline-Gerätetest.

Die Paketprüfung ist eine Entwicklungsgrundlage und ersetzt keine Sicherheitsprüfung oder getestete Installationsroutine.

## Vorbereitete Sicherungsschnittstelle

Aktuelle Entwicklungsstände stellen `POST /api/update/backup` für eine regulär authentifizierte lokale Browser-Sitzung bereit. Die Schnittstelle verwendet den bestehenden Sicherungsdienst, legt eine verwaltete Sicherung mit Präfix `klausurfix_update_` an und liefert deren Metadaten zurück. Sie benötigt keine Übermittlung des Wiederherstellungsschlüssels; dieser bleibt zum Wiederherstellen erforderlich. Ohne gültige Sitzung wird die Anfrage abgewiesen. Die bestehenden Prüfungen auf Host, Origin und `X-Requested-With` gelten unverändert.

Die Schnittstelle sichert ausschließlich bereits im Backend gespeicherte Daten. Sie speichert keine offenen Browser-Eingaben, reserviert den Datenbestand nicht gegen spätere Änderungen und beendet den Dienst nicht. Ein zukünftiger Update-Ablauf darf ihren Erfolg deshalb erst dann als Freigabe verwenden, wenn alle Ansichten nachweislich gespeichert und anschließend gegen weitere Änderungen gesperrt sind. Die Schnittstelle allein ist keine Freigabe für eine Installation.

## Speicheraufforderung in der App

Die Schaltfläche „Für Update sichern“ fordert die beim Backend registrierten Ansichten zur Speicherung auf, auch über Browserprofile und Origins hinweg. Während der Speicherabstimmung werden Eingaben in diesen Ansichten vorübergehend gesperrt. Klausurtabelle und Deckblatt verwenden ihre vorhandenen Speicherwege. Offene Dialogformulare sowie die Ansichten zum Anlegen einer Klausur und für Einstellungen müssen zunächst gespeichert oder verlassen werden; sie werden nicht automatisch abgeschickt. Wenn eine registrierte Ansicht nicht antwortet oder ein Speichern fehlschlägt, wird keine Sicherung durch diesen Ablauf erstellt. Nach den Speicherbestätigungen wird eine verschlüsselte Update-Sicherung angelegt und der Stand der Abstimmung erneut geprüft; anschließend werden die Ansichten wieder freigegeben. Die Aufforderung beweist noch nicht, dass alle offenen Ansichten tatsächlich registriert sind, und startet weder Beenden noch Installation.

`UpdateViewRegistry` verwaltet die registrierten Ansichten und die Speicher-Runde im Arbeitsspeicher. Sie erwartet Antworten aller registrierten Ansichten und verwirft eine Runde, wenn sich die Menge der Ansichten ändert oder eine Speicherung fehlschlägt. Die Runde läuft nach 30 Sekunden ab. Eine sichere Behandlung von Seiten-Neustart, Browserabsturz und Dienstneustart fehlt. Die Registrierung ist kein Nachweis, dass alle Ansichten erfasst sind, und keine Freigabe für Sicherung oder Installation.

## Vorbereitete Backend-Schreibsperre

`UpdateWriteGate` serialisiert schreibende HTTP-API-Aufrufe. Eine interne Sperre wartet, bis ein laufender Aufruf beendet ist, und weist weitere schreibende Aufrufe mit HTTP 423 ab, bis die Sperre freigegeben wird. Lesende Aufrufe bleiben möglich. Derzeit existiert ausdrücklich **kein API-Endpunkt**, der die Sperre für ein Update aktiviert; die bisherige Speicheraufforderung reicht dafür noch nicht als Nachweis. Die Sperre erfasst außerdem keine Hintergrundarbeiten außerhalb der HTTP-Pipeline. Ein späterer Ablauf muss die Sperre bei Fehler und Abbruch zuverlässig freigeben und Sicherung, Sperrung, Ablaufzeit, laufende Hintergrundarbeiten und das Beenden gemeinsam abstimmen. Weder eine gehaltene Sperre noch die Sicherungsschnittstelle allein sind eine Installationsfreigabe.

`UpdatePreparation` verbindet intern diese Sperre mit der Erstellung einer verschlüsselten verwalteten Update-Sicherung. Es wartet zuerst auf laufende HTTP-Schreibvorgänge, hält die Sperre während der Sicherung und gibt sie bei Fehler oder Abbruch wieder frei. Bei Erfolg bleibt sie bis zur ausdrücklichen Freigabe durch den aufrufenden Ablauf bestehen. Ein Abbruch während der synchronen Sicherung wird nach deren Abschluss erkannt; eine dabei entstandene Sicherung bleibt erhalten. Auch dieser interne Baustein prüft keine offenen Browseransichten und beendet die Anwendung nicht. Es gibt noch keine Schnittstelle für den Update-Assistenten.

## Vorbereitete Versionsablage

`Versionsablage/VersionStager` prüft ein signiertes Paket und stellt die Programmdateien unter einem neuen Ordner `versionen/<version>-<architektur>` bereit. Signatur, Architektur, Starter, Dienst und Dateihashes werden geprüft. Die Dateien werden zunächst in einem eigenen Vorbereitungsordner geschrieben und erst nach vollständigem Schreiben in den Versionsordner umbenannt; eine bestehende Version wird nicht überschrieben. Bei einem Fehler kann ein `.vorbereitung-...`-Ordner zurückbleiben. Er wird nicht automatisch gelöscht. Der Baustein schaltet noch keine Version aktiv, ändert keine Verknüpfung und greift nicht auf Arbeitsdaten zu. Er ist noch nicht mit dem Update-Assistenten verbunden; Sperre gegen mehrere Update-Prozesse, Wiederaufnahme nach Unterbrechung und Prüfungen auf dem Zielgerät stehen aus.

Der Starter kann zusätzlich den Dienst anhand einer lokalen Datei `aktive-version.txt` aus einem Versionsordner starten. `StoppedVersionInstaller` verbindet das Bereitstellen eines signierten Pakets mit der `ActiveVersion`-Umschaltung. Der Ablauf verwendet den Starter-Mutex, prüft Architektur, Dienst und Port und lehnt fehlende Dateien sowie eine automatische Rückstufung ab. Er setzt einen dafür geeigneten Starter voraus, den neu gebaute portable Pakete mit `update-starter-v1.txt` kennzeichnen. Ältere Installationen wie 0.1.4 können damit nicht automatisch umgestellt werden. Der Baustein beendet oder sichert die Anwendung nicht, garantiert nicht, dass außerhalb des Starter-Mutex kein Dienst gestartet wird, und ist noch nicht an den Update-Assistenten oder eine Sicherungsfreigabe angeschlossen. Er ist keine fertige Update-Installation.
