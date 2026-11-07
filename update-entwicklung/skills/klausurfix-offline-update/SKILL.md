---
name: klausurfix-offline-update
description: Entwickle und prüfe den lokalen Offline-Update-Ablauf für Klausurfix, einschließlich Speichern, Sicherung, Versionstausch und Windows-Start. Nicht für allgemeine Klausurfix-Funktionen verwenden.
---

# Klausurfix offline aktualisieren

Arbeite auf einen durchgehend benutzbaren Update-Ablauf hin. Die Lehrerin erstellt in Klausurfix eine verschlüsselte Sicherung und beendet die App selbst. Die einzelne Windows-Update-EXE prüft, ob der Dienst beendet und der Port frei ist, und begleitet danach Installation und Neustart offline. Auf ausdrückliche Vorgabe gilt der gestoppte Dienst für diesen Ablauf als Hinweis, dass Eingaben gespeichert sind. Ein Prüfprototyp oder eine Sammlung isolierter Komponenten erfüllt den Auftrag nicht.

## Vor Beginn

- Lies `AGENTS.md`, `update-entwicklung/README.md` und den relevanten Code; prüfe `git status`, vorhandene lokale Änderungen und den tatsächlichen Teststand. Die Dokumentation kann älter als der Code sein.
- Der Update-Assistent ist mit `StoppedVersionInstaller` verbunden, aber die einzelne EXE und ihr Vertrauensmodell sind noch nicht vollständig getestet. Das Beenden erfolgt manuell.
- Behalte den Übergang von 0.1.4 im Blick: Ihr Starter versteht den neuen Versionsverweis nicht und ihre Oberfläche kennt den neuen Abstimmungsweg nicht. Keine vollautomatische Kompatibilität behaupten, bevor sie umgesetzt und getestet ist.

## Sicherheits- und Ablaufgrenzen

- Personenbezogene Arbeitsdaten und Sicherungen liegen außerhalb des Programmordners. Vor der Installation eine vollständige verschlüsselte Sicherung erfolgreich erstellen und prüfen. Die Nutzerin beendet die App selbst; bei laufendem Dienst oder belegtem Port nicht installieren. Der Assistent beendet den Dienst nicht zwangsweise. Bestehende Session-, Origin- und CSRF-Prüfungen nicht umgehen.
- Auf ausdrücklichen Nutzerwunsch weder Herausgeberschlüssel noch Signatur verlangen. Paketstruktur, Architektur und Dateiprüfsummen prüfen; diese belegen keine Herkunft oder Unverfälschtheit gegenüber gezielter Änderung.
- Nur vollständig geprüfte Dateien in einem separaten Versionsordner bereitstellen; keine bestehende Version oder fremde Verknüpfung unbemerkt überschreiben. Starter, Update-Prozess und Dienststarts gegen Rennen koordinieren. Alte Versionen nach Datenmigration nicht automatisch starten, wenn sie das neue Datenformat nicht lesen können; Sicherung und Wiederherstellungsschlüssel erhalten.
- Architektur und Windows-Version des Zielgeräts vor der Verteilung feststellen. Keine Internetverbindung, Cloud, Telemetrie oder externe Laufzeitvoraussetzung für den Update-Ablauf einführen.

## Umsetzung und Prüfung

Verbinde vorhandene Teile zu einem tatsächlich ausführbaren Ablauf mit sichtbaren Schritten und Fehlerbehandlung. Berücksichtige dabei den Prüf-Assistenten, `UpdateViewRegistry`, `UpdateWriteGate`, `UpdatePreparation`, `BackupService`, `VersionStager`, `StoppedVersionInstaller`, `ActiveVersion` und den Starter. Nicht annehmen, dass diese bereits sicher miteinander verbunden sind.

Teste den zusammenhängenden Ablauf und Fehlerpfade: mehrere Browser und Tabs, ungespeicherte oder ungültige Eingaben, Neustarts und Abbrüche, Sperren, fehlgeschlagene Sicherung, beschädigtes Paket, belegten Port, parallelen Start, Aktivierung, Migration und Wiederherstellung. Nutze synthetische Daten und temporäre Verzeichnisse. Teste den tatsächlich gebauten Einzeldatei-Assistenten; reine Klassen- oder Mocktests beweisen den Gesamtablauf nicht. Offline-Betrieb, Oberfläche, ARM64/x64 und Windows Hello auf dem Surface als ungeprüft ausweisen, bis dort tatsächlich getestet.

Aktualisiere die Projektdokumentation nach überprüften Änderungen. Berichte konkret, was funktioniert und was noch fehlt; keine Fertigstellung oder Hardwareunterstützung aufgrund eines Prototyps oder Mocks behaupten.
