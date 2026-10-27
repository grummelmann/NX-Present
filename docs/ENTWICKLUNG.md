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
.\scripts\Build.ps1 -Installer -InstallerCompiler 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

Alternativ direkt mit dem SDK:

```powershell
dotnet restore src/NX-Present/NX-Present.csproj --locked-mode
dotnet build src/NX-Present/NX-Present.csproj -c Release --no-restore
dotnet run --project tests/NX-Present.Validation/NX-Present.Validation.csproj -c Release
dotnet publish src/NX-Present/NX-Present.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

Die Validierung prüft unter anderem URL- und Dateitypfilter, Showdateien, sichtbare Bewegung der Testbilder, statische Referenzlinien, das vollständig schwarze Schutzbild, Uhrzeitwechsel und die Einstellungsdatei. Sie verändert standardmäßig keine Windows-Audiogeräte. Der zusätzliche Parameter `--audio` ist ein bewusster Integrationstest, der Standardgeräte umstellt und wiederherstellt; er darf nicht parallel zu einer laufenden NX-Present-Sitzung ausgeführt werden.

`artifacts/` ist vollständig aus Git ausgeschlossen. Abhängigkeiten sind in `packages.lock.json` festgehalten. Ein Selbstbau bringt die .NET-Laufzeit mit. Die Bibliotheken werden dynamisch als getrennte Dateien eingebunden und nicht in einer einzelnen EXE versteckt.

## Aufbau

| Bereich | Aufgabe |
| --- | --- |
| `MainWindow*` | Steueroberfläche, Navigation, Bibliothek, gespeicherte Einstellungen und Fensterrahmen |
| `MediaSurface` | Bilder, Videos, Webseiten, Texte, Uhr und Testbilder in einer Fläche |
| `DisplaySafety` / `OutputWindow*` | Beamer-Fenster, Mausschutz, Schwarzbild und Inhaltswechsel |
| `TransitionOverlay` | Übergänge auch über native Video-/Browserfenster |
| `PatternView` / `ClockView` | Testsignale und Uhr |
| `AudioRouting` | Gemeinsames Windows-Standardgerät und Wiederherstellung |
| `SettingsStore` / `ShowFile` | Einstellungen und `.nxshow`-Dateien |
| `installer/` | Benutzerbezogenes Windows-Setup mit WebView2-Erkennung |

Beim ersten Start wird kein Ausgabe-Bildschirm geraten. Änderungen am Renderpfad sollten getrennt auf der tatsächlichen Beamer-Hardware beurteilt werden. Die FPS-Anzeige misst WPF-Aktualisierungen, keine physischen Präsentationszeitpunkte.

## Veröffentlichen

1. Version und Änderungsübersicht aktualisieren.
2. Projekt bauen, Validierung ausführen und Setup erzeugen.
3. Installation, Start, Live-Freigabe, Schwarzbild, Notausstieg und Deinstallation prüfen.
4. Fremdlizenzen und zugehörige Quellpakete prüfen und beilegen.
5. Ausschließlich im offiziellen Repository veröffentlichen: Installer, `SHA256SUMS.txt` und Fremdquellen.

Der Installer wird hier ohne eigenes Code-Signing-Zertifikat gebaut. Für eine später signierte Veröffentlichung muss der Herausgeber selbst ein passendes Zertifikat bzw. einen Signierdienst bereitstellen; Schlüssel gehören nicht in das Repository.

### Plugin-Katalog der Video-Engine

Das Buildskript übernimmt einen zu VideoLAN 3.0.24 / Windows x64 gehörenden Plugin-Katalog. Vorher prüft es die SHA-256-Werte des Katalogs, der Video-Engine und aller 321 Plugin-DLLs. Die Zeitstempel der verifizierten Builddateien werden auf die Katalogwerte normalisiert; der Installer erhält diese Dateizeiten. Dadurch muss die Video-Engine beim ersten normalen Start nicht sämtliche Plugins zur Katalogerstellung laden.

Bei einem Wechsel der nativen VideoLAN-Version oder des Plugin-Satzes muss der Katalog neu erzeugt und geprüft werden. Einfach den Hashvergleich abzuschalten ist kein Versionswechsel. Die Anwendung selbst sperrt geänderte LGPL-Bibliotheken nicht; nach Änderungen kann VLC seinen eigenen Katalog neu erstellen.

Die zugehörigen Originalquellen lassen sich mit `scripts/Get-ThirdPartySources.ps1` anhand des eingecheckten Manifests beziehen. Der offizielle Release stellt sie zusätzlich als eigenes ZIP-Archiv bereit.
