using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NXPresent;

/// <summary>
/// The cue page. It is laid out like a QLab workspace: GO and stop on top, the cue list with its times in the middle,
/// the inspector of the selected cue below, and two monitors beside it. The selected cue is the playhead.
/// </summary>
public partial class CueModeView : UserControl
{
    public CueRundown Rundown { get; } = new();
    public CueEngine? Engine { get; private set; }
    private readonly DispatcherTimer timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
    private Switcher? switcher;
    private ListCollectionView? sourceView;
    private RunCue? edited;
    private bool syncing, outputActive;
    public TransitionKind DefaultTransition { get; set; } = TransitionKind.Dissolve;
    public int DefaultDurationMilliseconds { get; set; } = 500;
    /// <summary>A line for the status bar.</summary>
    public event Action<string>? Message;
    public event EventHandler? SaveRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? LibraryRequested;

    public CueModeView()
    {
        InitializeComponent();
        DataContext = Rundown;
        BuildSwatches();
        BuildHotkeys();
        Rundown.PropertyChanged += (_, _) => Refresh();
        Rundown.ContentsChanged += Refresh;
        timer.Tick += (_, _) => Engine?.Tick();
        IsVisibleChanged += (_, _) => { if (Engine != null) Engine.PreloadEnabled = IsVisible; };
        Refresh();
    }

    /// <summary>Shows the next cue and the program on two monitors of the given switcher and starts the engine that runs the list.</summary>
    public void Attach(Switcher target)
    {
        Detach();
        switcher = target;
        Engine = new CueEngine(Rundown, target) { PreloadEnabled = IsVisible };
        Engine.Message += Notify;
        Engine.StateChanged += Refresh;
        target.Changed += OnSwitcherChanged;
        NextMonitor.Attach(target);
        LiveMonitor.Attach(target);
        timer.Start();
        CompositionTarget.Rendering += OnFrame;
        Refresh();
    }
    public void Detach()
    {
        timer.Stop();
        CompositionTarget.Rendering -= OnFrame;
        if (switcher != null) switcher.Changed -= OnSwitcherChanged;
        switcher = null;
        if (Engine != null) { Engine.Message -= Notify; Engine.StateChanged -= Refresh; Engine.Dispose(); Engine = null; }
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
        TargetPicker.ItemsSource = sources;
    }
    public void SetOutputActive(bool active)
    {
        outputActive = active;
        UpdateStatus();
    }

    // ---- what the keyboard does on this page ----
    public void Go() { if (Engine != null && Rundown.Next != null) Engine.Go(); }
    public void Stop() => Engine?.Stop();
    public void MovePlayhead(int delta) => Rundown.MovePlayhead(delta);
    /// <summary>Fires the cue that has this key as its hotkey. Returns whether the key belonged to a cue.</summary>
    public bool TryHotkey(Key key)
    {
        var cue = Rundown.FindHotkey(CueHotkeys.FromKey(key));
        if (cue == null || Engine == null) return false;
        Engine.Fire(cue);
        return true;
    }

    private void OnFrame(object? sender, EventArgs e) => Engine?.Tick();
    private void OnSwitcherChanged(SwitcherChange change)
    {
        if ((change & (SwitcherChange.Feeds | SwitcherChange.Transition | SwitcherChange.Black)) != 0) UpdateStatus();
    }
    private void Notify(string text) => Message?.Invoke(text);

    // ---- the page follows the rundown ----
    private void Refresh()
    {
        if (RunList == null) return;
        var next = Rundown.Next;
        int index = next == null ? -1 : Rundown.Items.IndexOf(next);
        bool editable = next != null && !next.IsActive;
        CountLabel.Text = Rundown.Items.Count + (Rundown.Items.Count == 1 ? " CUE" : " CUES");
        EmptyHint.Visibility = Rundown.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!ReferenceEquals(edited, next))
        {
            if (edited != null) edited.PropertyChanged -= EditedChanged;
            edited = next;
            if (edited != null) edited.PropertyChanged += EditedChanged;
            CueEditor.DataContext = next;
            SyncEditor();
        }
        CueEditor.IsEnabled = editable;
        UpButton.IsEnabled = editable && index > 0;
        DownButton.IsEnabled = editable && index + 1 < Rundown.Items.Count;
        DuplicateButton.IsEnabled = RemoveButton.IsEnabled = editable;
        RenumberButton.IsEnabled = Rundown.Items.Count > 0;
        ResetButton.IsEnabled = Rundown.Items.Count > 0;
        AddButton.IsEnabled = SourceList.SelectedItem is Cue;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (RunList == null) return;
        var next = Rundown.Next;
        bool active = Engine?.IsActive == true;
        GoButton.IsEnabled = Engine != null && next != null;
        StopButton.IsEnabled = active;
        NextText.Text = next != null ? (next.Number.Length > 0 ? "Q" + next.Number + " · " : "") + next.DisplayName
            : Rundown.Items.Count == 0 ? "Kein Cue" : "Ende des Ablaufs";
        string live = switcher == null ? "" : switcher.BlackLevel >= 1 ? "LIVE · SCHWARZ + STUMM"
            : "LIVE · " + (Rundown.Live?.DisplayName ?? (switcher.Program.Cue == Cue.Black ? "Schwarz" : switcher.Program.Cue.Name)) + (outputActive ? "" : "  (keine Ausgabe)");
        int running = Engine?.ActiveCount ?? 0;
        RunStatus.Text = live + (running > 0 ? $" · {running} {(running == 1 ? "Cue läuft" : "Cues laufen")} oder {(running == 1 ? "wartet" : "warten")}" : "");
    }

    // ---- editing the list ----
    private bool CanEdit => Rundown.Next is { IsActive: false };
    private void AddSource()
    {
        if (SourceList.SelectedItem is not Cue cue) return;
        RunList.ScrollIntoView(Rundown.Add(cue, DefaultTransition, DefaultDurationMilliseconds));
    }
    private void AddClicked(object sender, RoutedEventArgs e) => AddSource();
    private void SourceDoubleClicked(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is FrameworkElement { DataContext: Cue }) AddSource(); }
    private void AddCueMenuClicked(object sender, RoutedEventArgs e)
    {
        AddCueButton.ContextMenu.PlacementTarget = AddCueButton;
        AddCueButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        AddCueButton.ContextMenu.IsOpen = true;
    }
    private void AddTypeClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is RunCueType type) RunList.ScrollIntoView(Rundown.Add(type));
    }
    private void UpClicked(object sender, RoutedEventArgs e) { if (CanEdit) Rundown.Move(-1); }
    private void DownClicked(object sender, RoutedEventArgs e) { if (CanEdit) Rundown.Move(1); }
    private void DuplicateClicked(object sender, RoutedEventArgs e) { if (CanEdit && Rundown.Duplicate() is { } copy) RunList.ScrollIntoView(copy); }
    private void RemoveClicked(object sender, RoutedEventArgs e) { if (CanEdit) Rundown.Remove(); }
    private void RenumberClicked(object sender, RoutedEventArgs e) { Rundown.Renumber(); Notify("Alle Cues sind neu nummeriert."); }
    private void SourceSearchChanged(object sender, TextChangedEventArgs e) => sourceView?.Refresh();
    private void SourceSelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    // ---- running ----
    private void GoClicked(object sender, RoutedEventArgs e) => Go();
    private void StopClicked(object sender, RoutedEventArgs e) => Stop();
    private void ResetClicked(object sender, RoutedEventArgs e)
    {
        Rundown.Reset();
        if (Rundown.Next != null) RunList.ScrollIntoView(Rundown.Next);
    }
    private void JumpKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        string text = JumpBox.Text.Trim();
        if (text.Length == 0) return;
        if (Rundown.GoTo(text)) { RunList.ScrollIntoView(Rundown.Next!); JumpBox.Clear(); RunList.Focus(); }
        else Notify($"Einen Cue mit der Nummer {text} gibt es nicht.");
    }
    private void SaveClicked(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, e);
    private void OpenClicked(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, e);
    private void LibraryClicked(object sender, RoutedEventArgs e) => LibraryRequested?.Invoke(this, e);

    // ---- the inspector ----
    private void InspectorTabChanged(object sender, RoutedEventArgs e)
    {
        if (PanelBasics == null || PanelTrigger == null) return;
        PanelBasics.Visibility = TabBasics.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelTime.Visibility = TabTime.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelAction.Visibility = TabAction.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelTrigger.Visibility = TabTrigger.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildSwatches()
    {
        foreach (var color in Enum.GetValues<CueColor>())
        {
            var swatch = new RadioButton { Style = (Style)FindResource("Swatch"), GroupName = "CueColor", Tag = color, Background = CuePalette.Of(color), ToolTip = CuePalette.Label(color) };
            AutomationProperties.SetName(swatch, "Farbe " + CuePalette.Label(color));
            swatch.Checked += (sender, _) => { if (!syncing && edited != null) edited.Color = (CueColor)((RadioButton)sender).Tag; };
            SwatchPanel.Children.Add(swatch);
        }
    }
    private void BuildHotkeys()
    {
        HotkeyPicker.Items.Add(new ComboBoxItem { Content = "Keine", Tag = "" });
        foreach (string key in CueHotkeys.All) HotkeyPicker.Items.Add(new ComboBoxItem { Content = key, Tag = key });
    }

    /// <summary>Brings the controls that are not bound to the selected cue (number, colour, hotkey) in line with it.</summary>
    private void SyncEditor()
    {
        if (NumberBox == null) return;
        syncing = true;
        try
        {
            NumberBox.Text = edited?.Number ?? "";
            foreach (RadioButton swatch in SwatchPanel.Children) swatch.IsChecked = edited != null && (CueColor)swatch.Tag == edited.Color;
            string key = edited?.Hotkey ?? "";
            HotkeyPicker.SelectedItem = HotkeyPicker.Items.Cast<ComboBoxItem>().First(x => (string)x.Tag == key);
        }
        finally { syncing = false; }
    }
    private void EditedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!syncing && e.PropertyName is nameof(RunCue.Number) or nameof(RunCue.Color) or nameof(RunCue.Hotkey)) SyncEditor();
    }

    private void NumberCommitted(object sender, RoutedEventArgs e) => CommitNumber();
    private void NumberKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CommitNumber();
        RunList.Focus();
    }
    private void CommitNumber()
    {
        if (syncing || edited == null || NumberBox.Text.Trim() == edited.Number) return;
        if (Rundown.TrySetNumber(edited, NumberBox.Text, out string error)) return;
        Notify(error);
        NumberBox.Text = edited.Number;
    }

    private void TimeCommitted(object sender, RoutedEventArgs e) => CommitTime((TextBox)sender);
    private void TimeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        CommitTime((TextBox)sender);
        RunList.Focus();
    }
    /// <summary>Takes a typed time into the cue, or puts the old one back when it cannot be read.</summary>
    private void CommitTime(TextBox box)
    {
        if (edited == null || box.GetBindingExpression(TextBox.TextProperty) is not { } binding) return;
        binding.UpdateSource();
        binding.UpdateTarget();
    }

    private void HotkeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || edited == null || HotkeyPicker.SelectedItem is not ComboBoxItem { Tag: string hotkey }) return;
        if (Rundown.TrySetHotkey(edited, hotkey, out string error)) return;
        Notify(error);
        Dispatcher.BeginInvoke(SyncEditor);
    }
}
