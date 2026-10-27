# Fremdkomponenten von NX-Present 0.3.1

Die Projektlizenz von NX-Present gilt ausschließlich für den eigenen Programmcode und eigene Inhalte. Sie schränkt die eigenständigen Rechte an den folgenden Komponenten nicht ein. Die Anwendung verwendet getrennte, dynamisch geladene Bibliotheken; diese werden nicht verschlüsselt oder gegen einen Austausch gesperrt.

| Komponente | Version | Lizenz und Quelle |
| --- | --- | --- |
| .NET / Windows Desktop Runtime | 10.0.14 | MIT und weitere Lizenzen; die vollständigen Laufzeithinweise liegen im installierten Programmordner als `LICENSE.txt` und `THIRD-PARTY-NOTICES.txt`; [dotnet/runtime](https://github.com/dotnet/runtime/tree/v10.0.14), [dotnet/wpf](https://github.com/dotnet/wpf/tree/v10.0.14) |
| LibVLCSharp und LibVLCSharp.WPF | 3.10.2 | LGPL-2.1-or-later; [Quellcode](https://github.com/videolan/libvlcsharp/tree/3.10.2), [Lizenztext](licenses/LibVLCSharp-LICENSE.txt) |
| VideoLAN.LibVLC.Windows | 3.0.24 | Das verwendete NuGet-Paket wird von VideoLAN als LGPL-2.1-or-later bereitgestellt. Das separate Paket `.GPL` wird nicht verwendet. [Paket](https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.24), [VLC-Quellen](https://download.videolan.org/pub/videolan/vlc/3.0.24/vlc-3.0.24.tar.xz), [Paketierung](https://github.com/mfkl/libvlc-nuget) |
| Native Video-/Audio-Abhängigkeiten von VLC | Versionen gemäß VLC-3.0.24-Contrib-Rezepten | Eigene MIT-/BSD-/LGPL- und weitere Lizenztexte in ihren unveränderten Quellarchiven. Die VLC-Quellen enthalten die zugehörigen Buildrezepte und Patches unter `contrib/src` sowie Windows-Buildskripte unter `extras/package/win32`. |
| Microsoft.Web.WebView2 SDK | 1.0.4314.35 | Microsoft-Lizenz; [SDK-Lizenz](licenses/WebView2-SDK-LICENSE.txt), [NuGet-Paket](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35) |
| Microsoft Edge WebView2 Runtime / Bootstrapper | Evergreen | Unveränderter offizieller Microsoft-Bootstrapper im Installer; die Runtime wird nur bei Bedarf von Microsoft installiert. [Verteilungshinweise](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution) |

## Quellcode und Hinweise

Neben dem Windows-Installer stellt die offizielle Veröffentlichung ein separates Archiv **NX-Present-ThirdParty-Sources-0.3.1.zip** bereit. Es enthält die unveränderten VLC- und LibVLCSharp-Quellen, den verwendeten Paketierungsquellstand, Quellarchive der Video-/Audio-Abhängigkeiten und ein Manifest mit Bezugsadressen und Prüfsummen. Diese Quellen und die darin enthaltenen Lizenz- und Copyrightdateien behalten ihre jeweiligen ursprünglichen Lizenzen. Sie fallen ausdrücklich nicht unter das Weitergabeverbot für NX-Present.

Die Windows-NuGet-Paketierung trennt GPL-Plugins in ein eigenes Paket. Die verwendete LGPL-Ausgabe schließt diese Plugins aus; das Paketierungsverfahren ist im mitgelieferten Quellstand dokumentiert. Die unveränderten VLC-Quellen enthalten zusätzlich GPL-lizenzierte Programmteile, die nicht Bestandteil der NX-Present-Anwendung sind. Die Beigabe des GPL-Lizenztextes für diese Quellen bedeutet keine Umlizenzierung von NX-Present unter die GPL.

Sie dürfen die LGPL-Bibliotheken entsprechend ihrer Lizenz ersetzen oder verändern. Die dafür erforderliche Fehlersuche bzw. Rückentwicklung des verbundenen Programms wird durch die NX-Present-Lizenz nicht untersagt. Für einen eigenen nichtkommerziellen Build steht der Anwendungscode einschließlich Buildskripten im offiziellen Repository bereit.

## Werkzeuge zum Erstellen des Installers

Inno Setup 7 erstellt das Setup und das Deinstallationsprogramm. Die Laufzeit enthält dessen ursprüngliche Copyright-Hinweise. [Inno Setup](https://jrsoftware.org/), [Quellen und Lizenz](https://github.com/jrsoftware/issrc). Git, GitHub CLI, .NET SDK und Compilerwerkzeuge werden nicht als Entwicklungspakete auf dem PC des Endnutzers installiert.

## Generierter VLC-Plugin-Katalog

`installer/cache/plugins-3.0.24-x64.dat` ist ein von VLC erzeugter Metadatenkatalog zu den mitgelieferten, unveränderten LGPL-Plugins. Er enthält relative Pluginpfade und Modulbeschreibungen. Das danebenliegende Manifest bezeichnet Version, Architektur, Dateizeitstempel und SHA-256-Werte. Die Quelldateien für das Katalogformat stehen in den VLC-Quellen unter `src/modules/cache.c`; der Katalog wird nicht als eigener NX-Present-Programmcode unter eine abweichende Lizenz gestellt.
