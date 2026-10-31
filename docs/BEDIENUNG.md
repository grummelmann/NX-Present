# Bedienung

## Anschließen und einrichten

Verbinde Hauptmonitor und Beamer. Windows muss die Anzeigen **erweitern**, damit die Bedienung und die Leinwand getrennte Flächen erhalten. Wähle beim ersten Start unter **Setup → Leinwand und Ausgabeschutz** den tatsächlichen Beamer. Die Nummer allein ist nicht auf jedem PC dieselbe.

Mit **Ausgabe starten** beginnt die Projektion schwarz. **Maus und andere Fenster von der Leinwand fernhalten** aktiviert den Ausgabeschutz. Der globale Ausstieg **Strg + Alt + F10** bleibt erreichbar.

Der Mischer arbeitet auch ohne Ausgabe. Dann zeigt der Programm-Monitor, was auf der Leinwand stünde. So lässt sich ein Ablauf an einem einzelnen Monitor proben.

## Der Mischer

Die Seite **Mischer** ist wie ein Videomischer aufgebaut.

- **Vorschau-Monitor (links, grün):** die Quelle, die als Nächstes auf Programm gehen soll. Ein Video wartet auf seinem ersten Bild und hat unter dem Bild eine Wiedergabeleiste, um die Stelle zu prüfen. Webseiten, Texte, Uhr und Testbilder laufen live.
- **Programm-Monitor (rechts, rot):** genau das Bild, das auf der Leinwand steht, in Echtzeit. Bei einem Übergang zeigt er die Mischung samt Prozentwert.
- **Vorschau-Zeile:** eine Taste je Quelle. Ein Klick lädt die Quelle in die Vorschau. Die Tasten 1 bis 9 wählen die ersten neun Quellen des Pools, 0 wählt Schwarz.
- **Programm-Zeile:** zeigt, was auf Programm liegt. Die Tasten sind mit dem **Schloss** gesperrt, damit kein Fehlklick die Leinwand ändert. Entsperrt schneidet ein Klick sofort auf diese Quelle.
- **Medienpool:** alle Quellen als Karten mit Vorschaubild. Ein Klick lädt die Quelle in die Vorschau; über das Kontextmenü (rechte Maustaste) schneidest du sie sofort auf Programm oder entfernst sie aus dem Pool.

### CUT, AUTO und Überblender

- **CUT** (Eingabetaste) schaltet die Vorschau sofort auf Programm.
- **AUTO** (Leertaste, Strg + Enter) blendet mit dem gewählten Übergang und der eingestellten **Dauer** über. Ein Video startet erst mit der Blende, sein Ton blendet mit. Der Knopf leuchtet rot und zeigt den Fortschritt.
- **Überblender:** Ziehe ihn von links nach rechts, um von Hand zu mischen. Am rechten Ende wird die Vorschau zum Programm; zurück am Anfang wird der Übergang abgebrochen. Drückst du AUTO bei halb gezogenem Überblender, läuft der Rest der Blende automatisch.
- **Übergangsarten:** MIX (Überblenden), DIP (über Schwarz), WIPE → und WIPE ↓ (Wischen), IRIS und PUSH (Schieben).
- **CUT während einer Blende** beendet sie sofort.

Nach jedem Übergang tauschen Programm und Vorschau die Plätze: Die bisherige Quelle liegt nun in der Vorschau. Ein Video steht dort wieder auf seinem ersten Bild. So kannst du mit AUTO zwischen zwei Quellen hin und her blenden.

### Schwarz

- **FTB** (F) blendet das Programm weich nach Schwarz, auch der Ton. Ein zweiter Druck blendet zurück. Die Dauer stellst du unter Setup ein.
- **SCHWARZ + STUMM** (Strg + B) wirkt sofort und schaltet den Ton ab. Videos laufen im Hintergrund weiter.

Der Vorschau-Monitor bleibt bei beidem unverändert.

### Ton und Bild

Im Bereich **Audio** rechts stellst du die Hauptlautstärke ein, schaltest **Stumm** (M) und wählst, ob Videos wiederholt werden. Unter **Bild** legst du fest, wie Bilder und Videos auf die Fläche passen (Einpassen, Füllen, Strecken) und ob Testbilder animiert sind.

## Quellen hinzufügen

**+ Hinzufügen** im Medienpool bietet Bilder und Videos, Webseiten, Text und die Uhr. Dateien lassen sich auch in das Fenster ziehen. Fotos aus Kameras und Telefonen stehen aufrecht, auch wenn sie im Hochformat aufgenommen wurden.

Eine Webseite läuft in einem unsichtbaren Browser und erscheint wie jede andere Quelle. Sie lässt sich in dieser Fassung nicht bedienen; **Webseite neu laden** findest du rechts unter **Quelle in der Vorschau**. Der Ton einer Webseite schaltet bei einer Blende um, ohne zu überblenden.

## Cue Mode: einen Ablauf fahren

Öffne **Cues** in der Seitenleiste. Wähle links eine Quelle aus dem Medienpool und klicke **+ Zum Ablauf**. Eine Quelle kann mehrfach im Ablauf stehen. Über **Neue Inhalte im Mischer hinzufügen** wechselst du zurück zum Pool.

Die mittlere Liste ist die Reihenfolge deiner Show. Wähle einen Schritt und verschiebe ihn mit **↑ / ↓**, dupliziere oder entferne ihn. Rechts siehst du den nächsten Cue im Vorschau-Monitor und das Programm darunter. Dort legst du auch Bezeichnung, Regienotiz, Übergang und Dauer des Schritts fest.

**GO** (Leertaste, Strg + Enter) lädt den markierten Cue in die Vorschau und blendet mit seinem Übergang über. Erst nach dem vollständigen Übergang wird er als LIVE markiert und der folgende Cue vorbereitet. Nach dem letzten Cue läuft das Programm weiter; der Ablauf springt nicht automatisch an den Anfang. **Von Anfang** lädt den ersten Cue erneut in die Vorschau, ohne das Programm zu ändern.

Mit **Show speichern** sicherst du Pool und Ablauf gemeinsam als `.nxshow`. Bestehende Showdateien lassen sich weiter öffnen. Dateien mit Cue-Ablauf verwenden Format 2. Änderungen werden zusätzlich mit der Sitzung gespeichert. Nach einem Neustart wird kein Cue automatisch live geschaltet.

## Ton

Unter **Setup → Gemeinsames Audiogerät** wählst du das Ausgabegerät und bestätigst mit **Ton anwenden**. Das verändert das Windows-Standardgerät für alle Programme; laufende Videos und Webseiten folgen ihm. Die vorherige Windows-Auswahl wird beim normalen Beenden wiederhergestellt.

Die Hauptlautstärke regelt Videodateien. Webseiten haben ihre eigene Lautstärke. **Stumm** gilt für beide. Die Vorschau ist stumm, bis die Blende beginnt.

## Speichern, Umziehen und Beenden

Die Konfiguration wird automatisch unter `%LOCALAPPDATA%\NX-Present` gespeichert. Dazu gehören Pool, Gerätewahl, Übergang und Dauer, Lautstärke und Fenstergröße. Bei einem späteren Start kann die gespeicherte Beamer-Ausgabe automatisch mit Schwarzbild einschalten. Die zuletzt gewählte Vorschau wird wieder geladen, geht aber nie von selbst auf Programm.

Eine `.nxshow`-Datei sichert den Pool zusätzlich. Sie enthält Verweise auf Medien. Beim Umziehen müssen diese Dateien ebenfalls mitgenommen werden. `examples/Demo.nxshow` zeigt Testbilder, Uhr und Text ohne zusätzliche Medien.

**Strg + Alt + F10** schließt nur die Ausgabe. **Programm beenden** unter **Setup** oder das X schließt die ganze Anwendung und gibt die Maus frei.

Ein Update erfolgt durch den nächsten offiziellen Installer. Die Deinstallation entfernt die Anwendung; persönliche Einstellungen und Browserprofile bleiben erhalten.

## Wenn etwas nicht funktioniert

- **CUT und AUTO sind grau:** Es ist keine Quelle in der Vorschau, oder sie lädt noch. Der Vorschau-Monitor zeigt LÄDT oder FEHLER mit dem Grund.
- **Ein Video bleibt schwarz oder ruckelt:** Prüfe, ob die Datei in einem anderen Player läuft. Sehr hohe Auflösungen werden auf Full HD verkleinert, belasten die CPU aber trotzdem.
- **Eine Webseite bleibt leer:** Prüfe Adresse, Internetverbindung und die Microsoft-Edge-WebView2-Laufzeit. Es werden nur HTTP- und HTTPS-Adressen unterstützt.
- **Falsche Anzeige:** Schließe die Ausgabe, wähle unter Setup den richtigen Bildschirm und starte sie erneut.
- **Maus bleibt begrenzt:** Nutze **Strg + Alt + F10**.
- **Programm-Tasten reagieren nicht:** Sie sind gesperrt. Das Schloss neben PROGRAMM gibt sie frei.
- **Medien fehlen nach dem Umziehen:** Lege die verknüpften Dateien wieder am passenden Ort ab oder füge sie neu hinzu.
- **Bildrate schwankt:** Das Ziel garantiert keine durchgehend konstante Ausgabe. Auflösung, Grafiktreiber, Auslastung und Anzeigehardware beeinflussen das Ergebnis.
