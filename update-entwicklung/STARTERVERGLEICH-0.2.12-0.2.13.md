# Startervergleich: ARM64 0.2.12 und 0.2.13

## Beobachtung auf dem Surface

Nach dem Update 0.2.13 meldete die Nutzerin bzw. der Nutzer, dass Windows `Klausurfix.exe` blockiert und die Anwendung nicht mehr startet. Die anschließend erstellte portable Version mit den Programmdateien aus 0.2.13 und dem Starter aus 0.2.12 funktioniert nach Rückmeldung der Nutzerin bzw. des Nutzers. Welche konkrete Windows-Schutzfunktion und welche Entscheidung oder Regel die Blockierung ausgelöst hat, wurde nicht anhand des Schutzverlaufs oder Ereignisprotokolls verifiziert. Aus dem erfolgreichen Start folgt keine allgemeine Freigabe für andere Geräte oder spätere Updates.

## Geprüfte Unterschiede

Die beiden Starter stammen aus `app/Klausurfix.exe` der lokal vorhandenen Updatepakete der jeweiligen Version. Beide sind 129.296.552 Byte groß und laut `Get-AuthenticodeSignature` nicht signiert. Die SHA-256-Prüfsummen sind verschieden:

| Starter | SHA-256 | Dateiversion | Produktversion |
| --- | --- | --- | --- |
| 0.2.12 | `77E2F69BD8C7BFCB58F86C399B2E021A32CD1B5F5B45D32526CE4999F7800229` | 0.2.12.0 | 0.2.12+9072fba26568ace7fbf88aaaf0561bde9ee35cd1 |
| 0.2.13 | `DA351BB590BA9899A01A0671860A918662CC5DABF04C02CBCE6CB771BE5CC6A7` | 0.2.13.0 | 0.2.13+9072fba26568ace7fbf88aaaf0561bde9ee35cd1 |

Der Starter im portablen Sonderpaket `Klausurfix-0.2.13-win-arm64-Starter-0.2.12.zip` wurde anhand seiner Prüfsumme als bytegleich mit dem Starter des Updatepakets 0.2.12 geprüft. Die Programmdateien im Sonderpaket stammen ansonsten aus dem Updatepaket 0.2.13; dessen 67 Quelldateien wurden beim Erstellen gegen das Paketmanifest geprüft. Das Sonderpaket ist separat vom installierten Update und ersetzt dessen Starter nicht.

Der Vergleich der versionierten Dateien `src/Klausurfix.Launcher/` und `update-entwicklung/Versionsablage/ActiveVersion.cs` zwischen Git-Stand `9072fba` und `9d62147` zeigt keine Änderungen. Die eingebettete Produktversionsinformation beider Binaries nennt `9072fba`; sie belegt für sich allein weder einen vollständig sauberen Buildstand noch die Ursache der Blockierung. Eine funktionale Differenz der ausführbaren Dateien ist durch diesen Vergleich nicht ausgeschlossen.

Das damalige Buildskript veröffentlichte den Starter für jedes Paket erneut und übergab dabei `-p:Version=$packageVersion`. Mindestens die Versionsmetadaten änderten sich. Neue Binärdateien können von Windows anders eingestuft werden; ob die geänderte Version, eine andere Byteänderung oder ein anderer Umstand hier ausschlaggebend war, ist nicht nachgewiesen. Prüfsummen belegen Bytegleichheit, aber weder Herkunft noch Vertrauenswürdigkeit.

## Vorgabe für weitere Releases

- Den auf dem Surface erfolgreich getesteten ARM64-Starter 0.2.12 mit obiger Prüfsumme als bekannte Ausgangsdatei erhalten. Das lokale Paket `release/0.2.12/Klausurfix-Update-0.2.12-win-arm64.zip` wird von Git ignoriert und ist durch den Git-Push nicht gesichert; die Datei muss separat erhalten bleiben.
- Bei einem ARM64-Release ohne notwendige Starteränderung den Starter nicht allein wegen einer neuen App-Versionsnummer neu erzeugen oder austauschen. Einen geprüften, exakt bytegleichen Starter wiederverwenden, Paketmanifest und eingebettetes Updatepaket entsprechend erzeugen und vor Verteilung seine Prüfsumme mit der obigen vergleichen. Die App-Version weiterhin unabhängig über den Dienst anzeigen.
- Vor Wiederverwendung Kompatibilität mit dem geplanten Updateablauf und Versionsstarter prüfen; x64 erfordert eine eigene passende und unabhängig geprüfte Ausgangsdatei. Das Buildskript verwendet für ARM64 standardmäßig den geprüften Starter aus dem Updatepaket 0.2.12 und bricht bei fehlendem Paket, falscher Prüfsumme oder geänderten Starterquellen ab. `-BuildNewStarter` baut ausdrücklich einen neuen, auf dem Surface ungeprüften Starter. Der x64-Starter wird weiterhin neu gebaut und ist unabhängig zu prüfen.
- Änderungen am Starter oder seinen eingebundenen Dateien können eine neue Datei erforderlich machen. Dann ausdrücklich als neuen, auf dem Surface noch zu prüfenden Starter kennzeichnen und Installation, Starteraustausch, Dienststart und Blockierung auf dem Zielgerät testen. Der erfolgreiche portable Start beweist nicht, dass ein späterer installierter Updateablauf funktioniert.
- Keine Signatur, Smart-App-Control-Freigabe oder dauerhafte Nichtblockierung behaupten. Bei erneuter Blockierung den tatsächlichen Dateinamen, Schutzverlauf bzw. Code-Integrity-Ereignis und den Schutztyp feststellen; keine globalen Schutzfunktionen ungefragt deaktivieren.
