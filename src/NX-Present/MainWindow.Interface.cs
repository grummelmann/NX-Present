using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace NXPresent;

public partial class MainWindow
{
    private ICollectionView? libraryView;
    private string libraryFilter = "all";
    private bool interfaceReady;

    private void InitializeInterface()
    {
        libraryView = CollectionViewSource.GetDefaultView(cues);
        libraryView.Filter = MatchesLibrary;
        cues.CollectionChanged += (_, _) => RefreshLibrary();
        interfaceReady = true;
        RefreshLibrary();
        SyncInterfaceState();
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }
    private bool MatchesLibrary(object value)
    {
        if (value is not Cue cue) return false;
        bool category = libraryFilter switch
        {
            "media" => cue.Kind is not CueKind.Pattern and not CueKind.Clock,
            "patterns" => cue.Kind is CueKind.Pattern or CueKind.Clock,
            _ => true
        };
        string search = LibrarySearch?.Text.Trim() ?? "";
        return category && (search.Length == 0 || cue.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || cue.Category.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }
    private void RefreshLibrary()
    {
        if (!interfaceReady || libraryView == null) return;
        libraryView.Refresh();
        int shown = libraryView.Cast<object>().Count();
        CueCount.Text = shown == cues.Count ? $"{cues.Count} INHALTE" : $"{shown} VON {cues.Count} INHALTEN";
        EmptyLibrary.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void LibraryFilterChanged(object sender, RoutedEventArgs e)
    {
        libraryFilter = (sender as RadioButton)?.Tag as string ?? "all";
        RefreshLibrary();
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshLibrary();
    private void RevealInLibrary(Cue cue)
    {
        LibrarySearch.Clear();
        AllFilter.IsChecked = true;
        CloseLibraryEditor(this, new RoutedEventArgs());
        RefreshLibrary();
        CueList.SelectedItem = cue;
        CueList.ScrollIntoView(cue);
    }
    private void NavigationChanged(object sender, RoutedEventArgs e)
    {
        if (WorkspacePage == null || SettingsPage == null || PageTitle == null) return;
        HelpPage.Visibility = Visibility.Collapsed;
        bool settings = (sender as RadioButton)?.Tag as string == "settings";
        WorkspacePage.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsPage.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = settings ? "Einstellungen" : "Show Control";
        PageSubtitle.Text = settings ? "Geräte, Wiedergabe und Programm." : "Vorbereiten. Prüfen. Live schalten.";
        if (!settings && interfaceReady) UpdateLive();
    }
    private void OpenSettingsPage(object sender, RoutedEventArgs e) => SettingsNav.IsChecked = true;
    private void OpenAddMenu(object sender, RoutedEventArgs e)
    {
        AddContentButton.ContextMenu.PlacementTarget = AddContentButton;
        AddContentButton.ContextMenu.Placement = PlacementMode.Bottom;
        AddContentButton.ContextMenu.IsOpen = true;
    }
    private void OpenWebsiteEditor(object sender, RoutedEventArgs e) => OpenLibraryEditor(true);
    private void OpenTextEditor(object sender, RoutedEventArgs e) => OpenLibraryEditor(false);
    private void OpenLibraryEditor(bool website)
    {
        ShowNav.IsChecked = true;
        LibraryBrowser.Visibility = Visibility.Collapsed;
        LibraryEditor.Visibility = Visibility.Visible;
        WebsiteEditor.Visibility = website ? Visibility.Visible : Visibility.Collapsed;
        TextEditor.Visibility = website ? Visibility.Collapsed : Visibility.Visible;
        if (website) { UrlInput.Focus(); UrlInput.SelectAll(); }
        else CueTextInput.Focus();
    }
    private void CloseLibraryEditor(object sender, RoutedEventArgs e)
    {
        LibraryEditor.Visibility = Visibility.Collapsed;
        LibraryBrowser.Visibility = Visibility.Visible;
    }
    private void TransitionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DurationPicker != null) DurationPicker.IsEnabled = TransitionPicker.SelectedIndex != (int)TransitionKind.Cut;
    }
    private void SyncInterfaceState()
    {
        if (!interfaceReady) return;
        bool active = output != null;
        OutputBadge.Text = active ? "AUSGABE AKTIV" : "AUSGABE AUS";
        OutputDot.Fill = (Brush)FindResource(active ? "Accent" : "Muted");
        StartQuickButton.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        StartQuickButton.IsEnabled = !liveBusy;
        var kind = output?.Surface.Current?.Kind;
        VideoTransport.Visibility = active && kind == CueKind.Video ? Visibility.Visible : Visibility.Collapsed;
        WebReloadButton.Visibility = active && kind == CueKind.Website ? Visibility.Visible : Visibility.Collapsed;
        bool black = output?.BlackedOut == true;
        BlackButton.Background = (Brush)FindResource(black ? "Accent" : "Panel");
        BlackButton.Foreground = (Brush)FindResource(black ? "Bg" : "Ink");
        BlackButton.BorderBrush = (Brush)FindResource(black ? "Accent" : "Muted");
        DurationPicker.IsEnabled = TransitionPicker.SelectedIndex != 0;
    }
    private void BackToShow(object sender, RoutedEventArgs e)
    {
        ShowNav.IsChecked = true;
        NavigationChanged(ShowNav, new RoutedEventArgs());
    }
    private void OpenHelpPage()
    {
        ShowNav.IsChecked = false;
        SettingsNav.IsChecked = false;
        WorkspacePage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        HelpPage.Visibility = Visibility.Visible;
        PageTitle.Text = "Hilfe";
        PageSubtitle.Text = "Bedienung und Tastenkürzel auf einen Blick.";
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
