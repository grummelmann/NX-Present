# Entwicklung

NX-Present ist eine Windows-Desktop-Anwendung mit C#, WPF, .NET 10, LibVLCSharp und Microsoft WebView2. Die Projektlizenz erlaubt lokale nichtkommerzielle Anpassungen; die Veröffentlichung eigener Programmdateien benötigt eine gesonderte Erlaubnis. GitHub-Forks sind im beschriebenen Rahmen erlaubt.

## Voraussetzungen

- Windows x64
- .NET SDK 10.0.403; `global.json` erlaubt neuere Patches dieses SDK-Featurebands
- Netzwerkzugriff auf NuGet für den ersten Restore
- Optional: Inno Setup 7 für die Installations-EXE

Visual Studio ist nicht erforderlich. Die Werkzeuge zum Bauen gehören nicht in den Git-Quellbaum. Binärdateien, Browserprofile, Einstellungen und Zugangsdaten werden nicht eingecheckt.

## Bauen und prüfen

```powershell
.\scripts\Build.ps1
.\scripts\Build.ps1 -Media
.\scripts\Build.ps1 -Installer -InstallerCompiler 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

Alternativ direkt mit dem SDK:

```powershell
dotnet restore src/NX-Present/NX-Present.csproj --locked-mode
dotnet build src/NX-Present/NX-Present.csproj -c Release --no-restore
dotnet run --project tests/NX-Present.Validation/NX-Present.Validation.csproj -c Release
dotnet publish src/NX-Present/NX-Present.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

`artifacts/` ist vollständig aus Git ausgeschlossen. Abhängigkeiten sind in `packages.lock.json` festgehalten. Ein Selbstbau bringt die .NET-Laufzeit mit. Die Bibliotheken werden dynamisch als getrennte Dateien eingebunden und nicht in einer einzelnen EXE versteckt.

## Prüfungen

`tests/NX-Present.Validation` ist ein Konsolenprogramm ohne Testframework. Jede Prüfung schreibt `PASS …` oder bricht mit der ersten Abweichung ab.

Die Standardvalidierung braucht weder Video-Engine noch Browser und umfasst:

- URL- und Dateitypfilter, Showdateien, Einstellungen, Cue-Liste (Nummern, Tasten, Zeitangaben, Speichern, Kompatibilität älterer Shows)
- die Cue-Engine mit künstlicher Uhr: GO und Marke, Pre-Wait, Auto-Continue und Auto-Follow, gleichzeitige Cues, Warteschlange hinter laufenden Übergängen, Stopp, ausgeschaltete Cues, fehlgeschlagene und langsam ladende Quellen, Schwarz-, Lautstärke- und Video-Cues, Vorladen
- Testbilder (sichtbare Bewegung, feste Referenzlinien, Schutz-Schwarzbild) und Uhr
- die Mischer-Logik mit künstlicher Uhr: Vorschau, CUT, AUTO, Überblender, Fade to Black, Tausch von Programm und Vorschau, Audio-Pegel, abgebrochene und fehlgeschlagene Quellen
- den Compositor pixelgenau für jede Übergangsart (Mix, Dip, beide Wischer, Iris, Schieben) und für Schwarz
- Standbilder mit EXIF-Ausrichtung und Größenbegrenzung
- den Update-Dienst gegen einen Ersatz für GitHub ohne Netzwerk: Versionsnummern, vertrauenswürdige Adressen, `SHA256SUMS.txt`, Entscheidung aus der Release-Antwort (neuer, gleich, älter, Vorabversion, fehlende Dateien, fremde Adressen, Fehlerantworten), Download mit Prüfsumme, abgebrochene und beschädigte Downloads, Aufräumen, Einstellungen
- das Hauptfenster in einem unsichtbaren Fenster außerhalb aller Monitore: Medienpool, Tasten-Reihen, CUT und AUTO in Echtzeit, T-Bar, gesperrte Programm-Zeile, FTB, Mindestgröße, Speichern der Einstellungen. Dabei entstehen Screenshots der Oberfläche im Ordner `runs/` neben dem Testprogramm.

Mit `--media` (oder `Build.ps1 -Media`) kommen die Prüfungen mit der echten Video-Engine und der WebView2-Laufzeit hinzu: ein selbst erzeugtes Testvideo (Bildfarben, Cue, Wiedergabe, Pause, Suchen, Schleife, erneutes Bereitstellen, Freigabe, defekte Datei), eine lokale animierte Webseite (Bilder, Bewegung, Freigabe des Browserfensters, nicht erreichbare Adresse) und ein Mischer mit echten Quellen. `--video <Dateipfad>` ergänzt einen Test mit einer eigenen Videodatei. `--media-only` führt nur die Mischer- und Medienprüfungen aus.

`--live-updates` fragt zusätzlich die echte Veröffentlichungsseite des Projekts ab (nur die Release-Auskunft, es wird nichts geladen) und prüft, dass `Updater` deren Setup und Prüfsummenliste als vertrauenswürdig erkennt. Ändert sich der Besitzer oder Name des Repositorys, muss `Updater.Repository` mitgezogen werden; die alte Adresse wird bewusst nicht weiter akzeptiert, weil ein frei gewordener Name von jemand anderem belegt werden könnte.

Der zusätzliche Parameter `--audio` ist ein bewusster Integrationstest, der Windows-Standardgeräte umstellt und wiederherstellt; er darf nicht parallel zu einer laufenden NX-Present-Sitzung ausgeführt werden.

## Aufbau

| Bereich | Aufgabe |
| --- | --- |
| `Mixer/Switcher` | Logik des Mischers: Programm, Vorschau, CUT, AUTO, Überblender, FTB, Audio-Pegel. Kennt keine Oberfläche; die Zeit kommt von außen |
| `Mixer/SourceFeed` und Ableitungen | Eine Quelle liefert Bilder und erzeugt für jeden Monitor eine eigene Ansicht derselben Bilder: `StillFeed`, `VectorFeed`, `VideoFeed`, `WebFeed` |
| `Mixer/VideoFeed` | LibVLC über die Speicherausgabe: jedes Bild landet in einer `WriteableBitmap`. Der Player wird nur über eine serielle Warteschlange bedient, nie vom UI-Thread |
| `Mixer/WebFeed` | Webseite in einem Browserfenster außerhalb aller Monitore; die Bilder kommen per Screencast des Browsers |
| `Mixer/MixerSurface` | Compositor: Programm, einlaufende Quelle, Übergang und Schwarz. Ausgabefenster und Monitore benutzen je einen |
| `Mixer/MonitorView`, `TBar`, `BusKey` | Monitor mit Tally und Wiedergabeleiste, Überblender, Taste für Zeilen und Pool |
| `MainWindow*` | Oberfläche des Mischers, Pool, Einstellungen, Cue-Anbindung und Fensterrahmen |
| `CueRundown` | Die Cue-Liste als Daten: Cues mit Nummer, Zeiten, Art und Weiter-Modus, Marke (`Next`), Live-Cue, Nummern und Tasten. Kennt weder Oberfläche noch Mischer |
| `CueEngine` | Fährt die Liste wie QLab: GO, Pre-Wait, Auto-Continue und Auto-Follow, mehrere Cues zugleich, Warteschlange der Übergänge, Stopp, Vorladen. Die Zeit kommt von außen |
| `CueModeView`, `CueFormat` | Die Cue-Seite (Liste, Inspektor, Monitore) und ihre Helfer: Zeiten lesen und anzeigen, Farben, Tasten, Konverter |
| `DisplaySafety` | Beamer-Fenster (`OutputWindow`), Mausschutz, Anzeigenliste |
| `PatternView` / `ClockView` / `CueVisual` | Testsignale, Uhr, Texttafeln und deren Vorschaubilder |
| `AudioRouting` | Gemeinsames Windows-Standardgerät und Wiederherstellung |
| `SettingsStore` / `ShowFile` | Einstellungen und `.nxshow`-Dateien |
| `Updater`, `MainWindow.Update` | Suche, Download und Start des Update-Setups; Setup-Karte, Statusleiste und Prüfung beim Start |
| `installer/` | Benutzerbezogenes Windows-Setup mit WebView2-Erkennung, stillem Update und Neustart der Anwendung |

### Der Signalweg

Jede Quelle ist ein `SourceFeed`. Alle Monitore und das Ausgabefenster bauen mit `CreateView` eine eigene Ansicht, die dieselbe Bitmap oder Zeichnung zeigt. Es gibt deshalb keine Bildschirmaufnahme, keine Überlagerungsfenster und keine eingefrorene Vorschau: Der Programm-Monitor ist ein zweiter Compositor über denselben Bildern wie die Ausgabe.

`Switcher` hält die Quelle auf Programm und die Vorschau. Ein Übergang arbeitet mit einem Fortschritt von 0 bis 1; `MixerSurface` setzt daraus Deckkraft, Masken oder Verschiebung. Nach dem Abschluss tauschen Programm und Vorschau die Plätze, und die bisherige Quelle wird mit `Arm()` zurückgestellt (ein Video wartet wieder auf seinem ersten Bild).

### Video

`VideoFeed` setzt LibVLC auf die Speicherausgabe (`RV32`). Das Bild wird in der Größe des Videos nach Seitenverhältnis (einschließlich Pixelformat) auf höchstens 1920 × 1080 festgelegt. VLC schreibt in drei native Puffer; der UI-Thread kopiert das jeweils letzte fertige Bild in die `WriteableBitmap`. Ein angehaltenes Bild zeichnet VLC immer wieder neu; es wird nur wenige Male kopiert. Alle Befehle (Abspielen, Anhalten, Suchen, Lautstärke) laufen über eine serielle Warteschlange. Zeit, Länge und Zustand liest die Oberfläche aus zwischengespeicherten Werten. Beim Freigeben werden Player und Puffer erst nach dem letzten Rückruf freigegeben; beim Beenden wartet `VideoFeed.WaitForShutdown`.

### Webseiten

`WebFeed` startet ein `WebView2` in einem Fenster bei Position −12000, −12000. Chromium würde ein solches Fenster als verdeckt behandeln und das Zeichnen einstellen, deshalb setzt `WebHost` die Startparameter `--disable-features=CalculateNativeWinOcclusion`, `--disable-backgrounding-occluded-windows`, `--disable-renderer-backgrounding` und `--disable-background-timer-throttling`. Die Seite wird mit `Emulation.setDeviceMetricsOverride` auf 1920 × 1080 festgelegt, die Bilder liefert `Page.startScreencast` als JPEG. Bleibt der Screencast stumm, nimmt `CapturePreviewAsync` einzelne Bilder auf.

### Updates

`Updater` fragt `https://api.github.com/repos/nexoGroups/NX-Present/releases/latest` ab (ohne Anmeldung, höchstens 60 Anfragen je Stunde und Adresse) und bietet eine Veröffentlichung nur an, wenn

- sie weder Entwurf noch Vorabversion ist und ihr Tag `vX.Y.Z` (oder `X.Y.Z`) lautet, ohne Zusatz wie `-beta`,
- die Version größer ist als die laufende (`Updater.CurrentVersion`, aus `<Version>` in der `.csproj`),
- sie genau die Dateien `NX-Present-Setup-X.Y.Z-x64.exe` und `SHA256SUMS.txt` enthält und beide Adressen mit `https://github.com/nexoGroups/NX-Present/releases/` beginnen.

Der Download geht in `%LOCALAPPDATA%\NX-Present\Updates\<name>.part`, wird auf Größe und SHA-256 laut `SHA256SUMS.txt` geprüft und erst dann umbenannt. Die Prüfsumme stammt aus derselben Veröffentlichung; sie erkennt beschädigte Downloads, keine manipulierte Veröffentlichung. Ein Austausch gegen ein signiertes Setup (Authenticode-Prüfung vor dem Start) ist der nächste Schritt, sobald ein Zertifikat vorliegt.

Zum Installieren gibt die Anwendung ihre Einzelinstanz-Sperre frei (`Local\NX-Present-Controller`, sonst bricht das Setup wegen `AppMutex` ab), startet das Setup mit `/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /relaunch=1 /waitpid=<Prozess-ID> /LOG="…\setup-X.Y.Z.log"` und beendet sich. Das Setup wartet in `InitializeSetup` bis zu 30 Sekunden auf die Prozess-ID, ersetzt die Dateien und startet NX-Present über den `[Run]`-Eintrag mit `Check: RelaunchRequested`. Scheitert der Start des Setups, wird die Sperre zurückgeholt. Eine Kopie ohne `unins000.exe` neben der EXE (Selbstbau) wird nie ersetzt. Vor dem Start wird `UpdateAttempt` in den Einstellungen gesetzt; ist die laufende Version danach noch älter, gilt der Versuch als fehlgeschlagen und wird nicht automatisch wiederholt.

Die automatische Installation beim Start gilt nur, solange das Programm schwarz ist und kein Übergang läuft (`UpdateIsSafe`); sie wird direkt vor dem Start des Setups noch einmal geprüft.

## Veröffentlichen

1. Version (`<Version>` in `src/NX-Present/NX-Present.csproj`) und Änderungsübersicht aktualisieren.
2. Projekt bauen, Validierung mit `-Media` ausführen und Setup erzeugen.
3. Installation, Start, Ausgabe mit echtem Beamer, CUT, AUTO, Video und Webseite, Schwarzbild, Notausstieg, Update von der vorherigen Version (Setup-Karte und Start-Prüfung) und Deinstallation prüfen.
4. Fremdlizenzen und zugehörige Quellpakete prüfen und beilegen.
5. Ausschließlich im offiziellen Repository veröffentlichen, als Veröffentlichung mit dem Tag `vX.Y.Z`, ohne Haken bei „Vorabversion“: `NX-Present-Setup-X.Y.Z-x64.exe`, `SHA256SUMS.txt` (beide aus `artifacts/release`, die Namen dürfen nicht geändert werden) und Fremdquellen. Fehlt eine der beiden ersten Dateien, bieten Anwendungen die Version nicht als Update an.

Der Installer wird hier ohne eigenes Code-Signing-Zertifikat gebaut. Für eine später signierte Veröffentlichung muss der Herausgeber selbst ein passendes Zertifikat bzw. einen Signierdienst bereitstellen; Schlüssel gehören nicht in das Repository.

### Plugin-Katalog der Video-Engine

Das Buildskript übernimmt einen zu VideoLAN 3.0.24 / Windows x64 gehörenden Plugin-Katalog. Vorher prüft es die SHA-256-Werte des Katalogs, der Video-Engine und aller 321 Plugin-DLLs. Die Zeitstempel der verifizierten Builddateien werden auf die Katalogwerte normalisiert; der Installer erhält diese Dateizeiten. Dadurch muss die Video-Engine beim ersten normalen Start nicht sämtliche Plugins zur Katalogerstellung laden.

Bei einem Wechsel der nativen VideoLAN-Version oder des Plugin-Satzes muss der Katalog neu erzeugt und geprüft werden. Einfach den Hashvergleich abzuschalten ist kein Versionswechsel. Die Anwendung selbst sperrt geänderte LGPL-Bibliotheken nicht; nach Änderungen kann VLC seinen eigenen Katalog neu erstellen.

Die zugehörigen Originalquellen lassen sich mit `scripts/Get-ThirdPartySources.ps1` anhand des eingecheckten Manifests beziehen. Der offizielle Release stellt sie zusätzlich als eigenes ZIP-Archiv bereit.
