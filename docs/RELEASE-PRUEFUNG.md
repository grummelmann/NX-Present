# Releaseprüfung 0.4.0

Stand: 31.10.2026, gebaut mit `scripts/Build.ps1 -Installer -Media`.

## Geprüft

- Build: erfolgreich, 0 Warnungen und 0 Fehler.
- Validierung: 446 Prüfungen erfolgreich (Logik, Übergänge pixelgenau, Oberfläche, Cue-Liste, Updater; mit `-Media` auch echtes Video und echte Webseiten).
- Installer: `NX-Present-Setup-0.4.0-x64.exe` kompiliert (Inno Setup, 83 MB), Prüfsumme in `SHA256SUMS.txt`.
- Quellarchiv der Fremdkomponenten: `NX-Present-ThirdParty-Sources-0.4.0.zip`. Die Abhängigkeiten sind seit 0.3.1 unverändert (LibVLCSharp 3.10.2, VideoLAN.LibVLC.Windows 3.0.24, WebView2 SDK 1.0.4314.35), das Archiv von 0.3.1 wurde unter neuem Namen übernommen und hat dieselbe Prüfsumme.

## Nicht geprüft

- Das Setup wurde nicht ausgeführt, weil es die installierte Kopie ersetzen würde. Update von der Vorversion (Start, Setup → Updates zeigt die neue Version, „Nach Updates suchen“ meldet „aktuell“) steht aus.
- Selbstaktualisierung auf einem echten PC über eine veröffentlichte Version.
- Verhalten unter Smart App Control (Windows kann ein nicht signiertes Setup blockieren).
- Echter Beamer und Bildrate bei hoher Last.
- Tonausgabe bei laufendem Wechsel des Windows-Geräts.
- Eigene Code-Signatur: nicht vorhanden.
