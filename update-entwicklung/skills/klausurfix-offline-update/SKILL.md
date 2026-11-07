---
name: klausurfix-offline-update
description: Entwickle und prüfe den lokalen Offline-Update-Ablauf für Klausurfix, einschließlich Speichern, Sicherung, Versionstausch und Windows-Start. Nicht für allgemeine Klausurfix-Funktionen verwenden.
---

# Klausurfix offline aktualisieren

Arbeite auf einen durchgehend benutzbaren Update-Ablauf hin. Eine Lehrerin erhält eine einzelne Windows-EXE und soll ohne Internetverbindung sehen, wie Klausurfix Eingaben speichert, verschlüsselt sichert, beendet, aktualisiert und neu startet. Ein Prüfprototyp oder eine Sammlung isolierter Komponenten erfüllt diesen Auftrag nicht. Bei einem ausdrücklichen Implementierungsauftrag selbstständig weiterarbeiten und keine neuen Zwischenbausteine als Abschluss ausgeben.

## Vor Beginn

- Lies `AGENTS.md`, `update-entwicklung/README.md` und den relevanten Code; prüfe `git status`, vorhandene lokale Änderungen und den tatsächlichen Teststand. Die Dokumentation kann älter als der Code sein.
- Die Update-EXE, der Prüf-Assistent und ein erfolgreicher Pakettest sind unterschiedliche Dinge. Zurzeit ist der Assistent nur ein Prüfprototyp. `StoppedVersionInstaller` ist noch nicht mit ihm oder dem automatischen Beenden verbunden.
- Behalte den Übergang von 0.1.4 im Blick: Ihr Starter versteht den neuen Versionsverweis nicht und ihre Oberfläche kennt den neuen Abstimmungsweg nicht. Keine vollautomatische Kompatibilität behaupten, bevor sie umgesetzt und getestet ist.

## Sicherheits- und Ablaufgrenzen

- Personenbezogene Arbeitsdaten und Sicherungen liegen außerhalb des Programmordners. Vor der Installation ausstehende Eingaben aller relevanten Ansichten zuverlässig speichern, weitere Änderungen verhindern und eine vollständige verschlüsselte Sicherung erfolgreich erstellen und prüfen. Bei Zweifeln oder Fehlern nicht installieren und den Dienst nicht zwangsweise beenden.
- Die bestehende serverseitige Ansichtsregistrierung und die zeitlich begrenzte Eingabesperre sind keine vollständige Installationsfreigabe: Seiten-, Browser- und Dienstneustarts sowie nicht registrierte oder eingefrorene Ansichten müssen sicher behandelt werden. Die bestehenden Session-, Origin- und CSRF-Prüfungen nicht umgehen. Sperre, Sicherung, Hintergrundarbeiten und geordnetes Beenden gemeinsam betrachten.
- Paketinhalt anhand einer unabhängig vertrauenswürdigen Herausgeberidentität prüfen. Ein im Update mitgelieferter öffentlicher Schlüssel oder Fingerabdruck allein authentifiziert das Update nicht. Schlüssel und private Zertifikate niemals ins Repository legen. Offline-Prüfung und ihre Grenzen, insbesondere fehlende aktuelle Zertifikatssperrprüfung, klar benennen.
- Nur vollständig geprüfte Dateien in einem separaten Versionsordner bereitstellen; keine bestehende Version oder fremde Verknüpfung unbemerkt überschreiben. Starter, Update-Prozess und Dienststarts gegen Rennen koordinieren. Alte Versionen nach Datenmigration nicht automatisch starten, wenn sie das neue Datenformat nicht lesen können; Sicherung und Wiederherstellungsschlüssel erhalten.
- Architektur und Windows-Version des Zielgeräts vor der Verteilung feststellen. Keine Internetverbindung, Cloud, Telemetrie oder externe Laufzeitvoraussetzung für den Update-Ablauf einführen.

## Umsetzung und Prüfung

Verbinde vorhandene Teile zu einem tatsächlich ausführbaren Ablauf mit sichtbaren Schritten und Fehlerbehandlung. Berücksichtige dabei den Prüf-Assistenten, `UpdateViewRegistry`, `UpdateWriteGate`, `UpdatePreparation`, `BackupService`, `VersionStager`, `StoppedVersionInstaller`, `ActiveVersion` und den Starter. Nicht annehmen, dass diese bereits sicher miteinander verbunden sind.

Teste den zusammenhängenden Ablauf und Fehlerpfade: mehrere Browser und Tabs, ungespeicherte oder ungültige Eingaben, Neustarts und Abbrüche, Sperren, fehlgeschlagene Sicherung, beschädigtes oder falsch signiertes Paket, belegten Port, parallelen Start, Aktivierung, Migration und Wiederherstellung. Nutze synthetische Daten und temporäre Verzeichnisse. Teste den tatsächlich gebauten Einzeldatei-Assistenten; reine Klassen- oder Mocktests beweisen den Gesamtablauf nicht. Offline-Betrieb, Oberfläche, ARM64/x64 und Windows Hello auf dem Surface als ungeprüft ausweisen, bis dort tatsächlich getestet.

Aktualisiere die Projektdokumentation nach überprüften Änderungen. Berichte konkret, was funktioniert und was noch fehlt; keine Fertigstellung oder Hardwareunterstützung aufgrund eines Prototyps oder Mocks behaupten.
