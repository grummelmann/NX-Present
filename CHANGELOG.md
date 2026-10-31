# Änderungen

## 0.4.0 – Bildmischer (noch nicht veröffentlicht)

Die Bedienung ist neu aufgebaut wie ein Videomischer.

- **Echter Viewer:** Der Programm-Monitor zeigt in Echtzeit das Bild der Ausgabe, auch bei laufenden Videos, Webseiten, Uhr und animierten Testbildern. Der Vorschau-Monitor zeigt die nächste Quelle live; ein Video wartet auf seinem ersten Bild.
- **CUT, AUTO und Überblender:** Sofortschnitt, automatischer Übergang mit einstellbarer Dauer und ein Überblender für die Handblende. AUTO aus halb gezogenem Überblender läuft den Rest. Nach jedem Übergang tauschen Programm und Vorschau die Plätze.
- **Neue Übergänge:** Wischen waagerecht und senkrecht, Iris und Schieben, zusätzlich zu Überblenden und Über Schwarz. Der Ton von Videos blendet mit.
- **FTB** (weich nach Schwarz, mit Ton) und **Schwarz + stumm** (sofort).
- **Programm- und Vorschau-Zeile** mit einer Taste je Quelle, Ziffern 1 bis 9 und 0, Tally in Rot und Grün. Die Programm-Zeile ist gesperrt, bis man sie freigibt.
- **Medienpool** mit Vorschaubildern, Suche, Filtern und Kontextmenü; Webseiten und Texte werden in einem Dialog hinzugefügt.
- **Neue Oberfläche** nach dem Vorbild von Mischer- und Grading-Software: dunkle neutrale Flächen, rechts ein Bereich für Ausgabe, Audio, Bild und Quelle, die Seitenwahl unten statt links.
- **Cue Mode als Cue-Liste nach dem Vorbild von QLab:** Eine Marke zeigt den nächsten Cue; GO startet ihn und setzt die Marke sofort weiter. Jeder Cue hat Nummer, Pre-Wait, Dauer und Post-Wait sowie Auto-Continue oder Auto-Follow, sodass eine Kette mit einem GO läuft. Cue-Arten: Quelle, Schwarz, Lautstärke, Video steuern, Warten und Notiz. Dazu kommen Ausschalten, Farbmarken, Tasten für den Direktabruf, Gehe zu Cue, Nummerierung, ein Inspektor mit Reitern, Fortschrittsbalken in der Liste, Vorladen der nächsten Quelle und STOPP (Esc). Weil es einen Programmausgang gibt, warten Quell-Cues in einer Warteschlange auf das Ende des laufenden Übergangs. GO funktioniert auch ohne Ausgabefenster. Showdateien mit Cue-Liste haben Format 3; ältere Dateien bleiben lesbar und werden von oben nummeriert.
- **Beispiel-Show:** `examples/Demo.nxshow` enthält jetzt eine kleine Cue-Liste mit Notiz, Auto-Follow-Kette, Taste und Schwarzblende.
- **Ohne Beamer nutzbar:** Der Mischer arbeitet vollständig ohne Ausgabefenster.
- **Automatische Updates:** Unter Setup zeigt eine neue Karte die installierte Version und bietet „Nach Updates suchen“ und „Jetzt aktualisieren“. Beim Start sucht NX-Present außerdem im Hintergrund auf der offiziellen GitHub-Veröffentlichungsseite nach einer neueren Version und installiert sie, solange nichts live ist; läuft schon Inhalt, wartet das Update auf einen Klick, und ein Hinweis „UPDATE x.y.z“ erscheint in der Statusleiste. Beides lässt sich unter Setup ausschalten. Das Setup wird gegen die veröffentlichte `SHA256SUMS.txt` geprüft, nur Adressen unter `github.com/nexoGroups/NX-Present/releases` werden akzeptiert, und ein fehlgeschlagener Versuch wird nicht bei jedem Start wiederholt. Eine selbst gebaute Kopie wird nicht überschrieben.
- **Überarbeiteter Installer:** Das Setup wartet bei einem Update auf das Ende der laufenden Anwendung, installiert still über die vorhandene Version und startet NX-Present danach neu (`/relaunch=1`, `/waitpid=…`, Protokoll im Update-Ordner). Die Plugin-Dateien der Video-Engine werden vor der Installation ersetzt statt ergänzt, ein zweites Setup gleichzeitig wird verhindert, und die veraltete Einstellung `WizardResizable` ist entfernt.

Behobene Fehler der Vorgängerfassung (nach Durchsicht des Codes):

- Der Überblender konnte beim Start eines Übergangs die Meldung „Die Breite und die Höhe dürfen nicht negativ sein“ auslösen. Stand der Griff weniger als einen Pixel von der Ausgangsstellung entfernt, rechnete die Zeichnung des gelben Füllbalkens mit einer negativen Breite, und WPF brach das Layout ab; die Meldung blockierte dann als Dialog die Bedienung. Das trat bei AUTO und bei GO im Cue Mode auf, wenn der erste Bildaufbau genau in diesen Bereich fiel. Der Balken wird jetzt erst ab einem Pixel gezeichnet, und eine Prüfung deckt die Griffstellungen und Breiten ab.
- Die AUTO-Taste wurde während einer laufenden Blende abgeblendet statt rot und voll sichtbar zu leuchten.

- Der Live-Monitor zeigte bei Videos und Webseiten ein Bildschirmfoto der Ausgabe, einmal pro Sekunde. Es gibt jetzt keine Bildschirmaufnahme mehr; beide Monitore zeigen die Bilder selbst in Echtzeit.
- Übergänge liefen über ein zusätzliches Fenster mit einem Bildschirmfoto als Zwischenbild. Ein abgebrochener Wechsel ließ die Ausgabe schwarz. Der Übergang wird jetzt im Compositor gerechnet und bricht nie nach Schwarz ab.
- Ein neuer Wechsel sperrte die Bedienung, bis der Inhalt geladen war, bei einer Webseite bis zu 15 Sekunden. Quellen laden jetzt in der Vorschau; AUTO ist erst freigegeben, wenn die Quelle bereit ist.
- Die Vorschau war nur ein Standbild und nicht das, was später lief. Sie zeigt jetzt die Quelle selbst.
- Videos und Webseiten steckten in eigenen Fenstern, die sich nicht mit anderen Bildern überblenden ließen. Alles wird jetzt im selben Bildaufbau gezeichnet.
- Ein Wechsel der Tonausgabe lud die laufenden Medien neu. Das ist nicht mehr nötig.
- Fotos aus Kameras und Telefonen konnten im Hochformat falsch herum stehen, weil die Ausrichtung der Aufnahme nicht beachtet wurde. Sehr große Bilder werden außerdem auf eine sinnvolle Größe verkleinert.
- In Einstellungen und Showdateien wurden abgeleitete Felder (Symbol, Kategorie, Detail) mitgeschrieben. Alte Dateien bleiben lesbar.
Technik: Videos laufen über die Speicherausgabe der Video-Engine, Webseiten über den Screencast eines unsichtbaren Browsers. Beides liefert Bilder an dieselben Monitore wie die Ausgabe. Die Prüfungen decken Logik, Übergänge pixelgenau, die Oberfläche und, mit `-Media`, echtes Video und echte Webseiten ab.

Hinweise: Das Setup einer Veröffentlichung ist nicht signiert. Die Prüfsumme erkennt beschädigte oder unvollständige Downloads, aber keine manipulierte Veröffentlichung; auf einem PC mit Smart App Control kann Windows das Setup eines Updates blockieren, dann meldet NX-Present das und lässt die Datei im Update-Ordner liegen. Videos werden softwareseitig dekodiert und auf höchstens Full HD verkleinert. Webseiten lassen sich nicht bedienen; ihr Ton schaltet bei Übergängen um, statt zu überblenden. Das Verhalten mit einem echten Beamer, die Tonausgabe bei laufendem Wechsel des Windows-Geräts und die Bildrate bei hoher Last wurden nicht an Hardware geprüft.

## Unveröffentlicht (vor 0.4.0)

- Neue Seite „Cue Mode“: Ablauf mit mehrfach nutzbaren Quellen, Reihenfolge, Bezeichnungen, Regienotizen, Übergängen pro Cue und manuellem GO. Ablauf und Sitzung werden gespeichert; alte Showdateien bleiben lesbar.

## 0.3.1 – Erste öffentliche Veröffentlichung

- Schwarz-neongelbe Bedienoberfläche mit Bibliothek, Vorbereitung und Leinwand.
- Eigenständige Seiten für Setup und Hilfe.
- Bilder, Videos, Webseiten, Texte, Uhr und animierte Testbilder.
- Live-Freigabe mit Sofort, Überblenden und Über Schwarz.
- Globale Freigabe der Ausgabe über Strg + Alt + F10.
- Gespeicherte Konfiguration und automatischer Schwarzstart für eine ausdrücklich gewählte Ausgabe.
- Erststart ohne automatische Bildschirm-Vermutung.
- Windows-Installer mit gebündelter .NET-Laufzeit und Video-Engine sowie bedarfsgerechter WebView2-Installation.

60 FPS ist das Animationsziel; durchgehend konstante 60 FPS sind nicht bestätigt.
