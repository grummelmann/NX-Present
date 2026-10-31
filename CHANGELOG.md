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
- **Cue Mode:** GO lädt den nächsten Cue in die Vorschau und blendet mit seinem Übergang über. Zwei Monitore zeigen nächsten Cue und Programm. GO funktioniert auch ohne Ausgabefenster.
- **Ohne Beamer nutzbar:** Der Mischer arbeitet vollständig ohne Ausgabefenster.

Behobene Fehler der Vorgängerfassung (nach Durchsicht des Codes):

- Der Live-Monitor zeigte bei Videos und Webseiten ein Bildschirmfoto der Ausgabe, einmal pro Sekunde. Es gibt jetzt keine Bildschirmaufnahme mehr; beide Monitore zeigen die Bilder selbst in Echtzeit.
- Übergänge liefen über ein zusätzliches Fenster mit einem Bildschirmfoto als Zwischenbild. Ein abgebrochener Wechsel ließ die Ausgabe schwarz. Der Übergang wird jetzt im Compositor gerechnet und bricht nie nach Schwarz ab.
- Ein neuer Wechsel sperrte die Bedienung, bis der Inhalt geladen war, bei einer Webseite bis zu 15 Sekunden. Quellen laden jetzt in der Vorschau; AUTO ist erst freigegeben, wenn die Quelle bereit ist.
- Die Vorschau war nur ein Standbild und nicht das, was später lief. Sie zeigt jetzt die Quelle selbst.
- Videos und Webseiten steckten in eigenen Fenstern, die sich nicht mit anderen Bildern überblenden ließen. Alles wird jetzt im selben Bildaufbau gezeichnet.
- Ein Wechsel der Tonausgabe lud die laufenden Medien neu. Das ist nicht mehr nötig.
- Fotos aus Kameras und Telefonen konnten im Hochformat falsch herum stehen, weil die Ausrichtung der Aufnahme nicht beachtet wurde. Sehr große Bilder werden außerdem auf eine sinnvolle Größe verkleinert.
- In Einstellungen und Showdateien wurden abgeleitete Felder (Symbol, Kategorie, Detail) mitgeschrieben. Alte Dateien bleiben lesbar.
Technik: Videos laufen über die Speicherausgabe der Video-Engine, Webseiten über den Screencast eines unsichtbaren Browsers. Beides liefert Bilder an dieselben Monitore wie die Ausgabe. Die Prüfungen decken Logik, Übergänge pixelgenau, die Oberfläche und, mit `-Media`, echtes Video und echte Webseiten ab.

Hinweise: Videos werden softwareseitig dekodiert und auf höchstens Full HD verkleinert. Webseiten lassen sich nicht bedienen; ihr Ton schaltet bei Übergängen um, statt zu überblenden. Das Verhalten mit einem echten Beamer, die Tonausgabe bei laufendem Wechsel des Windows-Geräts und die Bildrate bei hoher Last wurden nicht an Hardware geprüft.

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
