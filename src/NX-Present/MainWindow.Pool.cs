using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Win32;

namespace NXPresent;

public partial class MainWindow
{
    private readonly BusKey blackKey = new(Cue.Black, 0);
    private readonly ObservableCollection<BusKey> poolKeys = new();
    private readonly ObservableCollection<BusKey> busKeys = new();
    private ICollectionView poolView = null!;
    private string poolFilter = "all";
    private string websiteDraft = "https://", textDraft = "";

    private void InitializePool()
    {
        poolView = CollectionViewSource.GetDefaultView(poolKeys);
        poolView.Filter = MatchesPool;
        PoolItems.ItemsSource = poolView;
        ProgramBus.ItemsSource = busKeys;
        PreviewBus.ItemsSource = busKeys;
        cues.CollectionChanged += (_, _) => RebuildKeys();
        AddDefaultSources();
    }
    private void AddDefaultSources()
    {
        (string Name, string Id)[] patterns = [("Farbbalken", "bars"), ("Raster und Ausrichtung", "grid"), ("Graustufen", "gray"), ("Schachbrett", "checker"), ("Weiß", "white"), ("Rot", "red"), ("Grün", "green"), ("Blau", "blue"), ("Schwarz", "black"), ("NX-Present Startbild", "logo")];
        foreach (var p in patterns) cues.Add(new Cue(CueKind.Pattern, p.Name, p.Id));
        cues.Add(new Cue(CueKind.Clock, "Uhr", "local"));
    }

    /// <summary>Keeps the keys of the buses and cards of the pool in step with the library, reusing the existing ones.</summary>
    private void RebuildKeys()
    {
        var unused = poolKeys.ToList();
        poolKeys.Clear();
        int number = 1;
        foreach (var cue in cues)
        {
            var key = unused.FirstOrDefault(k => k.Cue == cue);
            if (key != null) unused.Remove(key); else key = new BusKey(cue, number);
            key.Number = number++;
            key.RequestThumbnail();
            poolKeys.Add(key);
        }
        busKeys.Clear();
        busKeys.Add(blackKey);
        foreach (var key in poolKeys) busKeys.Add(key);
        if (interfaceReady) SyncTally();
        RefreshPool();
    }

    private bool MatchesPool(object value)
    {
        if (value is not BusKey key) return false;
        var cue = key.Cue;
        bool category = poolFilter switch
        {
            "media" => cue.Kind is not CueKind.Pattern and not CueKind.Clock,
            "patterns" => cue.Kind is CueKind.Pattern or CueKind.Clock,
            _ => true
        };
        string search = PoolSearch?.Text.Trim() ?? "";
        return category && (search.Length == 0 || cue.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || cue.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }
    private void RefreshPool()
    {
        if (PoolCount == null || poolView == null) return;
        poolView.Refresh();
        int shown = poolView.Cast<object>().Count();
        PoolCount.Text = shown == cues.Count ? $"{cues.Count} QUELLEN" : $"{shown} VON {cues.Count} QUELLEN";
        EmptyPool.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PoolFilterChanged(object sender, RoutedEventArgs e)
    {
        poolFilter = (sender as RadioButton)?.Tag as string ?? "all";
        RefreshPool();
    }
    private void PoolSearchChanged(object sender, TextChangedEventArgs e) => RefreshPool();

    private async void PoolCardClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BusKey key) await SelectPreview(key.Cue);
    }
    private static BusKey? KeyOf(object sender) => ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is FrameworkElement { DataContext: BusKey key } ? key : null;
    private async void PoolPreviewClicked(object sender, RoutedEventArgs e)
    {
        if (KeyOf(sender) is BusKey key) await SelectPreview(key.Cue);
    }
    private async void PoolCutClicked(object sender, RoutedEventArgs e)
    {
        if (KeyOf(sender) is BusKey key) await HotCut(key.Cue);
    }
    private void PoolRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (KeyOf(sender) is not BusKey key) return;
        if (cues.Remove(key.Cue)) { dirty = true; Status("Aus dem Pool entfernt. Die Datei bleibt auf dem Datenträger erhalten."); }
    }

    private void OpenAddMenu(object sender, RoutedEventArgs e)
    {
        AddContentButton.ContextMenu.PlacementTarget = AddContentButton;
        AddContentButton.ContextMenu.Placement = PlacementMode.Top;
        AddContentButton.ContextMenu.IsOpen = true;
    }
    private void AddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Bilder und Videos hinzufügen", Filter = "Bilder und Videos|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.wmv;*.mpeg;*.mpg;*.ts|Alle Dateien|*.*" };
        if (dialog.ShowDialog(this) == true) Import(dialog.FileNames);
    }
    private void FilesDropped(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) Import((string[])e.Data.GetData(DataFormats.FileDrop)); }
    private void Import(string[] paths)
    {
        int added = 0; string? error = null;
        Cue? last = null;
        foreach (var path in paths)
        {
            try { var cue = Cue.FromFile(path); cues.Add(cue); last = cue; added++; }
            catch (Exception ex) { error = ex.Message; }
        }
        if (added > 0) { dirty = true; AllFilter.IsChecked = true; PoolSearch.Clear(); _ = SelectPreview(last!); }
        Status(added + " Datei(en) hinzugefügt." + (error == null ? "" : " " + error));
    }
    private void AddWebsite(object sender, RoutedEventArgs e)
    {
        string? text = PromptWindow.Ask(this, "Webseite hinzufügen", "Internetadresse (http oder https)", websiteDraft);
        if (text == null) return;
        if (!Cue.TryWebsite(text, out string address)) { Status("Bitte eine gültige HTTP- oder HTTPS-Adresse eingeben."); return; }
        websiteDraft = text;
        AddCue(new Cue(CueKind.Website, new Uri(address).Host, address));
    }
    private void AddText(object sender, RoutedEventArgs e)
    {
        string? text = PromptWindow.Ask(this, "Text hinzufügen", "Text für die Leinwand", textDraft, multiline: true)?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        textDraft = text;
        AddCue(new Cue(CueKind.Text, text.Length > 45 ? text[..45] + "…" : text, text));
    }
    private void AddClock(object sender, RoutedEventArgs e) => AddCue(new Cue(CueKind.Clock, "Uhr", "local"));
    private void AddCue(Cue cue)
    {
        cues.Add(cue);
        dirty = true;
        AllFilter.IsChecked = true;
        PoolSearch.Clear();
        _ = SelectPreview(cue);
    }

    // ---- show files ----
    private bool SaveCurrentShow(bool choosePath)
    {
        string? path = showPath;
        if (choosePath || path == null)
        {
            var dialog = new SaveFileDialog { Title = "NX-Present Show speichern", Filter = "NX-Present Show|*.nxshow", DefaultExt = ".nxshow", FileName = path == null ? "Meine Show.nxshow" : Path.GetFileName(path) };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        try { ShowFile.Save(path, cues, CueModePage.Rundown.Snapshot()); showPath = path; dirty = false; Status("Show mit Cue-Ablauf gespeichert: " + Path.GetFileName(path) + ". Mediendateien werden verknüpft, nicht eingebettet."); return true; }
        catch (Exception ex) { App.Log(ex); Status("Speichern fehlgeschlagen: " + ex.Message); return false; }
    }
    private void SaveShow(object sender, RoutedEventArgs e) => SaveCurrentShow(true);
    private bool ConfirmUnsaved()
    {
        if (!dirty || loadingShow) return true;
        var result = MessageBox.Show(this, "Änderungen an Pool und Cue-Ablauf speichern?", "NX-Present", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result == MessageBoxResult.No || (result == MessageBoxResult.Yes && SaveCurrentShow(false));
    }
    private void OpenShow(object sender, RoutedEventArgs e)
    {
        if (rundownBusy || switcher.IsTransitioning) { Status("Bitte den laufenden Übergang abwarten."); return; }
        var dialog = new OpenFileDialog { Title = "NX-Present Show öffnen", Filter = "NX-Present Show|*.nxshow" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var document = ShowFile.LoadDocument(dialog.FileName);
            var list = document.Cues;
            if (!ConfirmUnsaved()) return;
            loadingShow = true;
            cues.Clear();
            foreach (var cue in list) cues.Add(cue);
            CueModePage.Rundown.Restore(document.Rundown ?? []);
            loadingShow = false;
            showPath = dialog.FileName; dirty = false;
            int missing = list.Concat((document.Rundown ?? []).Select(x => x.Cue)).Distinct().Count(x => x.Kind is CueKind.Image or CueKind.Video && !File.Exists(x.Source));
            Status("Show geöffnet." + (missing > 0 ? " Achtung: " + missing + " Mediendatei(en) fehlen." : ""));
        }
        catch (Exception ex) { loadingShow = false; App.Log(ex); Status("Show konnte nicht geöffnet werden: " + ex.Message); }
    }
}
