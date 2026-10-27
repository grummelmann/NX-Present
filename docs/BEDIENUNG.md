# Bedienung

## Anschließen und einrichten

Verbinde Hauptmonitor und Beamer. Windows muss die Anzeigen **erweitern**, damit die Bedienung und die Leinwand getrennte Flächen erhalten. Wähle beim ersten Start unter **Setup → Leinwand & Ausgabeschutz** den tatsächlichen Beamer. Die Nummer allein ist nicht auf jedem PC dieselbe.

Mit **Ausgabe starten** beginnt die Projektion schwarz. **Maus und andere Fenster von der Leinwand fernhalten** aktiviert den Ausgabeschutz. Der globale Ausstieg **Strg + Alt + F10** bleibt erreichbar.

## Vorbereiten und live zeigen

Die Bibliothek links enthält deine Quellen. **Alle**, **Medien** und **Testbilder** sowie das Suchfeld helfen beim Finden. **+ Inhalt** bietet Bilder/Videos, Webseiten, Text, die Uhr und das NX-Startbild. Dateien lassen sich auch in das Steuerfenster ziehen.

Eine Auswahl lädt nur die Vorbereitung. Sie ist immer stumm. Mit **Live anzeigen** oder **Strg + Enter** übernimmst du sie auf die Leinwand. Vor dem Wechsel kannst du Übergang und Dauer auswählen. Bei **Sofort** ist keine Dauer nötig.

Webseiten und Texte haben einen kompakten Editor links. **Zur Vorschau hinzufügen** legt den Inhalt in der Bibliothek an. Webseiten beginnen auf der Live-Seite neu; Eingaben und Anmeldungen aus der Vorbereitung werden nicht übertragen.

## Während der Show

**Schwarz + stumm** wirkt sofort. Videos laufen im Hintergrund weiter. **Bild freigeben** zeigt den vorherigen Live-Inhalt wieder an. Die Transportknöpfe erscheinen bei Videos, **Webseite neu laden** bei einer Webseite.

Testbilder bleiben standardmäßig in Bewegung. Unter **Setup → Bilder & Testsignale** lässt sich die Animation gemeinsam für Vorbereitung und Ausgabe anhalten. Das Schutz-Schwarzbild bleibt unabhängig davon vollständig schwarz.

Die Uhr kann als eigener Inhalt live angezeigt werden. Sie wird nicht über andere Inhalte gelegt.

## Ton

Unter **Setup → Ton & Wiedergabe** wählst du das gemeinsame Ausgabegerät und bestätigst mit **Ton anwenden**. Das verändert das Windows-Standardgerät für alle Programme. Laufende Medien werden beim Wechsel neu geladen. Die vorherige Windows-Auswahl wird beim normalen Beenden wiederhergestellt.

Die Lautstärke in NX-Present regelt Videodateien. Webseiten haben ihre eigene Lautstärke. **Stumm** gilt für beide. Die Vorbereitung bleibt unabhängig davon stumm.

## Speichern, Umziehen und Beenden

Die Konfiguration wird automatisch unter `%LOCALAPPDATA%\NX-Present` gespeichert. Dazu gehören Bibliothek, Gerätewahl, Wiedergabeoptionen und Entwürfe. Bei einem späteren Start kann die gespeicherte Ausgabe automatisch mit Schwarzbild starten; vorbereitete Inhalte gehen erst nach deiner Freigabe live.

Eine `.nxshow`-Datei sichert die Bibliothek zusätzlich. Sie enthält Verweise auf Medien. Beim Umziehen müssen diese Dateien ebenfalls mitgenommen werden. `examples/Demo.nxshow` demonstriert Testbilder, Uhr und Text ohne zusätzliche Medien.

**Strg + Alt + F10** schließt nur die Ausgabe. **Programm beenden** unter **Setup → Sitzung & Dateien** oder das X schließt die ganze Anwendung und gibt die Maus frei.

Ein Update erfolgt durch den nächsten offiziellen Installer. Die Deinstallation entfernt die Anwendung; persönliche Einstellungen und Browserprofile bleiben erhalten.

## Wenn etwas nicht funktioniert

- **Kein Live-Knopf:** Prüfe, ob die Ausgabe läuft und ein Inhalt vorbereitet ist.
- **Falsche Anzeige:** Schließe die Ausgabe, wähle unter Setup den richtigen Bildschirm und starte sie erneut.
- **Maus bleibt begrenzt:** Nutze **Strg + Alt + F10**.
- **Webseiten laden nicht:** Prüfe Adresse, Internetverbindung und Microsoft Edge WebView2 Runtime. Es werden nur HTTP-/HTTPS-Adressen unterstützt.
- **Medien fehlen nach dem Umziehen:** Lege die verknüpften Dateien wieder am passenden Ort ab oder füge sie neu hinzu.
- **Live-Kontrollbild eines Videos ruckelt:** Das Kontrollbild im Steuerfenster wird ungefähr einmal pro Sekunde erfasst. Die eigentliche Beamer-Wiedergabe arbeitet getrennt davon.
- **Bildrate schwankt:** Das 60-FPS-Ziel garantiert keine durchgehend konstante Ausgabe. Auflösung, Grafiktreiber, Auslastung und Anzeigehardware beeinflussen das Ergebnis.
