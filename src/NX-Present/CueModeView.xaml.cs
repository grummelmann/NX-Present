using System;
using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace NXPresent;

public partial class CueModeView : UserControl
{
    public CueRundown Rundown { get; } = new();
    private ListCollectionView? sourceView;
    private bool busy, outputActive, black;
    private string? liveName;
    public TransitionKind DefaultTransition { get; set; } = TransitionKind.Dissolve;
    public int DefaultDurationMilliseconds { get; set; } = 500;
    public event EventHandler? GoRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? LibraryRequested;
    public CueModeView()
    {
        InitializeComponent();
        DataContext = Rundown;
        Rundown.PropertyChanged += (_, _) => Refresh();
        Rundown.ContentsChanged += Refresh;
        Refresh();
    }
    /// <summary>Shows the next cue and the program on two monitors of the given switcher.</summary>
    public void Attach(Switcher switcher)
    {
        NextMonitor.Attach(switcher);
        LiveMonitor.Attach(switcher);
    }
    public void Detach()
    {
        NextMonitor.Picture.Detach();
        LiveMonitor.Picture.Detach();
    }
    public void SetFit(Stretch fit)
    {
        NextMonitor.Picture.Fit = fit;
        LiveMonitor.Picture.Fit = fit;
    }
    public void SetSources(IList sources)
    {
        sourceView = new ListCollectionView(sources) { Filter = value => value is Cue cue && (string.IsNullOrWhiteSpace(SourceSearch.Text) || cue.Name.Contains(SourceSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) || cue.Category.Contains(SourceSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)) };
        SourceList.ItemsSource = sourceView;
    }
    public void SetOutputState(bool active, bool isBusy, string? name, bool isBlack)
    {
        outputActive = active; busy = isBusy; liveName = name; black = isBlack; Refresh();
    }
    private void Refresh()
    {
        if (RunList == null) return;
        var next = Rundown.Next;
        int index = next == null ? -1 : Rundown.Items.IndexOf(next);
        CountLabel.Text = Rundown.Items.Count + " CUES";
        EmptyHint.Visibility = Rundown.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CueEditor.DataContext = next;
        CueEditor.IsEnabled = next != null && !busy;
        RunList.IsEnabled = !busy;
        SourcePanel.IsEnabled = !busy;
        FileTools.IsEnabled = !busy;
        UpButton.IsEnabled = !busy && index > 0;
        DownButton.IsEnabled = !busy && index >= 0 && index + 1 < Rundown.Items.Count;
        DuplicateButton.IsEnabled = RemoveButton.IsEnabled = !busy && next != null;
        ResetButton.IsEnabled = !busy && Rundown.Items.Count > 0;
        // The mixer works without a projector, so GO does not wait for the output window.
        GoButton.IsEnabled = !busy && next != null;
        AddButton.IsEnabled = !busy && SourceList.SelectedItem is Cue;
        LiveLabel.Text = black ? "LIVE · SCHWARZ + STUMM" : "LIVE · " + (Rundown.Live?.DisplayName ?? liveName ?? "Schwarz") + (outputActive ? "" : "  (keine Ausgabe)");
        RunStatus.Text = busy ? "Übergang läuft …" : next != null ? $"Bereit: Cue {next.Number:00} von {Rundown.Items.Count} · {next.DisplayName}" : Rundown.Items.Count > 0 ? "Letzter Cue erreicht. Das Programm läuft weiter." : "Links Quellen zum Ablauf hinzufügen.";
    }
    private void SourceSearchChanged(object sender, TextChangedEventArgs e) => sourceView?.Refresh();
    private void SourceSelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();
    private void AddClicked(object sender, RoutedEventArgs e) { if (!busy && SourceList.SelectedItem is Cue cue) { var step = Rundown.Add(cue, DefaultTransition, DefaultDurationMilliseconds); RunList.ScrollIntoView(step); } }
    private void UpClicked(object sender, RoutedEventArgs e) { if (!busy) Rundown.Move(-1); }
    private void DownClicked(object sender, RoutedEventArgs e) { if (!busy) Rundown.Move(1); }
    private void DuplicateClicked(object sender, RoutedEventArgs e) { if (!busy) Rundown.Duplicate(); }
    private void RemoveClicked(object sender, RoutedEventArgs e) { if (!busy) Rundown.Remove(); }
    private void ResetClicked(object sender, RoutedEventArgs e) { if (!busy) Rundown.Reset(); }
    private void GoClicked(object sender, RoutedEventArgs e) { if (GoButton.IsEnabled) GoRequested?.Invoke(this, e); }
    private void SaveClicked(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, e);
    private void OpenClicked(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, e);
    private void LibraryClicked(object sender, RoutedEventArgs e) => LibraryRequested?.Invoke(this, e);
}
