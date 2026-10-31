using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NXPresent;

public partial class MainWindow
{
    // Win32 error that CreateProcess reports when an application control policy (Smart App Control, WDAC) refuses a file.
    private const int ApplicationControlBlock = 4551;

    private Updater? updater;
    private UpdateOffer? offeredUpdate;
    private CancellationTokenSource? updateWork;
    private bool updateBusy;
    private string? updateAttempt;
    private Version? failedUpdate;

    private void InitializeUpdates()
    {
        UpdateVersionText.Text = Updater.CurrentVersion.ToString(3);
        updateAttempt = savedSettings.UpdateAttempt;
        if (isolated)
        {
            UpdateStatusText.Text = "Im Testmodus ist die Update-Suche aus.";
            CheckUpdateButton.IsEnabled = false;
            return;
        }
        updater = new Updater();
        updater.CleanUp();
        if (updateAttempt != null)
        {
            if (Updater.TryParseVersion(updateAttempt, out var attempted) && attempted > Updater.CurrentVersion)
            {
                failedUpdate = attempted;
                UpdateStatusText.Text = "Das letzte Update auf Version " + attempted.ToString(3) + " wurde nicht abgeschlossen. Es wird nicht automatisch wiederholt. Protokoll: " + updater.DownloadFolder;
                Status("Das letzte Update auf Version " + attempted.ToString(3) + " wurde nicht abgeschlossen. Details unter Setup.");
            }
            else updateAttempt = null;
        }
    }

    private bool UpdateIsSafe => switcher.Program.Cue == Cue.Black && !switcher.IsTransitioning;

    /// <summary>Runs shortly after start: looks for an update and, when allowed and nothing is live, installs it.</summary>
    private async Task StartupUpdateAsync()
    {
        if (updater == null || CheckUpdatesAtStart.IsChecked != true) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            if (closing) return;
            var result = await CheckForUpdateAsync(manual: false);
            if (result?.Offer is not UpdateOffer offer) return;
            string version = offer.Version.ToString(3);
            if (AutoInstallUpdates.IsChecked != true || !updater.CanInstall) { Status("Update " + version + " ist verfügbar. Installieren unter Setup."); return; }
            if (failedUpdate != null && offer.Version <= failedUpdate) { Status("Update " + version + " ist verfügbar, der letzte Versuch schlug aber fehl. Installieren unter Setup."); return; }
            if (!UpdateIsSafe) { Status("Update " + version + " ist verfügbar. Es wird nicht automatisch installiert, weil gerade Inhalt läuft. Installieren unter Setup."); return; }
            await InstallUpdateAsync(offer, automatic: true);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void ShowUpdateBadge()
    {
        if (offeredUpdate == null) { UpdateBadge.Visibility = Visibility.Collapsed; return; }
        string version = offeredUpdate.Version.ToString(3);
        UpdateBadge.Content = "UPDATE " + version;
        UpdateBadge.ToolTip = "Version " + version + " ist verfügbar. Klick öffnet Setup.";
        UpdateBadge.Visibility = Visibility.Visible;
    }

    private void BeginUpdateWork(string text)
    {
        updateBusy = true;
        updateWork = new CancellationTokenSource();
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = text;
    }

    private void EndUpdateWork()
    {
        updateBusy = false;
        updateWork?.Dispose();
        updateWork = null;
        UpdateProgress.Visibility = Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = updater != null;
        InstallUpdateButton.IsEnabled = offeredUpdate != null && updater?.CanInstall == true;
        ShowUpdateBadge();
    }

    private async Task<UpdateCheck?> CheckForUpdateAsync(bool manual)
    {
        if (updater == null || updateBusy || closing) return null;
        BeginUpdateWork("Suche nach Updates …");
        try
        {
            var result = await updater.CheckAsync(Updater.CurrentVersion, updateWork!.Token);
            if (closing) return null;
            offeredUpdate = result.Offer;
            UpdateStatusText.Text = result.Message;
            if (result.Offer is UpdateOffer offer)
            {
                if (!updater.CanInstall) UpdateStatusText.Text += " Diese Kopie wurde nicht über das Setup installiert und lässt sich nicht selbst ersetzen. Lade das Setup von der Veröffentlichungsseite.";
                InstallUpdateButton.Visibility = Visibility.Visible;
                UpdateNotesButton.Visibility = Visibility.Visible;
                if (manual) Status("Update " + offer.Version.ToString(3) + " ist verfügbar.");
            }
            else
            {
                InstallUpdateButton.Visibility = Visibility.Collapsed;
                UpdateNotesButton.Visibility = Visibility.Collapsed;
                if (manual) Status(result.Message);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!closing) UpdateStatusText.Text = "Suche abgebrochen.";
            return null;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (!closing) UpdateStatusText.Text = "Die Suche nach Updates ist fehlgeschlagen: " + ex.Message;
            return null;
        }
        finally { EndUpdateWork(); }
    }

    private async Task InstallUpdateAsync(UpdateOffer offer, bool automatic)
    {
        if (updater == null || updateBusy || closing) return;
        if (!updater.CanInstall) { UpdateStatusText.Text = "Diese Kopie wurde nicht über das Setup installiert. Lade das Setup von der Veröffentlichungsseite."; return; }
        string version = offer.Version.ToString(3);
        BeginUpdateWork("Version " + version + " wird geladen …");
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateBadge.Content = "UPDATE LÄDT";
        UpdateBadge.ToolTip = "Klick bricht den Download ab.";
        UpdateBadge.Visibility = Visibility.Visible;
        string? setup = null;
        try
        {
            var progress = new Progress<double>(fraction =>
            {
                if (closing) return;
                int percent = (int)(fraction * 100);
                UpdateProgress.Value = percent;
                UpdateBadge.Content = "UPDATE " + percent.ToString(CultureInfo.InvariantCulture) + " %";
                UpdateStatusText.Text = "Version " + version + " wird geladen … " + percent.ToString(CultureInfo.InvariantCulture) + " %";
            });
            Status("Update " + version + " wird geladen. Das Programm läuft währenddessen normal weiter.");
            setup = await updater.DownloadAsync(offer, progress, updateWork!.Token);
            if (closing) return;
            if (!UpdateIsSafe)
            {
                if (automatic)
                {
                    UpdateStatusText.Text = "Version " + version + " ist geladen, wird aber nicht installiert, weil inzwischen Inhalt läuft. Installiere sie unter Setup, wenn die Veranstaltung vorbei ist.";
                    Status("Update " + version + " wartet auf deine Bestätigung unter Setup.");
                    return;
                }
                if (MessageBox.Show(this, "Das Programm zeigt gerade Inhalt oder blendet über. Beim Aktualisieren wird NX-Present beendet und die Leinwand wird schwarz.\n\nTrotzdem jetzt aktualisieren?", "NX-Present", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    UpdateStatusText.Text = "Version " + version + " ist geladen. Aktualisiert wird, wenn du erneut auf „Jetzt aktualisieren“ klickst.";
                    return;
                }
            }
            LaunchInstaller(setup, offer);
        }
        catch (OperationCanceledException)
        {
            if (!closing) { UpdateStatusText.Text = "Update abgebrochen."; Status("Update abgebrochen."); }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (!closing)
            {
                UpdateStatusText.Text = "Das Update konnte nicht geladen werden: " + ex.Message;
                Status("Update fehlgeschlagen: " + ex.Message);
            }
        }
        finally { EndUpdateWork(); }
    }

    /// <summary>Hands over to the setup: it waits for this process, installs over it and starts the new version.</summary>
    private void LaunchInstaller(string setup, UpdateOffer offer)
    {
        string version = offer.Version.ToString(3);
        var app = Application.Current as App;
        string? previousAttempt = updateAttempt;
        updateAttempt = version;
        SaveSettings();
        app?.ReleaseInstanceLock();
        try
        {
            updater!.LaunchInstaller(setup, offer.Version);
        }
        catch (Exception ex)
        {
            app?.RestoreInstanceLock();
            updateAttempt = previousAttempt;
            SaveSettings();
            App.Log(ex);
            string reason = ex is Win32Exception { NativeErrorCode: ApplicationControlBlock }
                ? "Windows hat das Setup blockiert (Anwendungssteuerung, zum Beispiel Smart App Control), weil es nicht signiert ist."
                : "Das Setup konnte nicht gestartet werden: " + ex.Message;
            UpdateStatusText.Text = reason + " Die geladene Datei liegt in " + updater!.DownloadFolder + ".";
            Status(reason);
            return;
        }
        Status("Update " + version + " wird installiert. NX-Present startet danach neu.");
        Close();
    }

    private void CheckUpdateClicked(object sender, RoutedEventArgs e) => _ = CheckForUpdateAsync(manual: true);
    private void InstallUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (offeredUpdate is UpdateOffer offer) _ = InstallUpdateAsync(offer, automatic: false);
    }
    private void UpdateNotesClicked(object sender, RoutedEventArgs e)
    {
        if (offeredUpdate?.ReleasePage is not Uri page || !Updater.IsTrustedUrl(page)) return;
        try { Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); Status("Die Seite konnte nicht geöffnet werden: " + ex.Message); }
    }
    private void UpdateBadgeClicked(object sender, RoutedEventArgs e)
    {
        if (updateBusy && updateWork != null) { updateWork.Cancel(); return; }
        SettingsNav.IsChecked = true;
    }
}
