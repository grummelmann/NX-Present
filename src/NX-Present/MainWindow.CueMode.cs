using System;
using System.Windows;

namespace NXPresent;

public partial class MainWindow
{
    private void InitializeCueMode()
    {
        CueModePage.Attach(switcher);
        CueModePage.SetSources(cues);
        CueModePage.Message += Status;
        CueModePage.SaveRequested += (sender, _) => SaveShow(sender!, new RoutedEventArgs());
        CueModePage.OpenRequested += (sender, _) => OpenShow(sender!, new RoutedEventArgs());
        CueModePage.LibraryRequested += (_, _) => MixerNav.IsChecked = true;
        CueModePage.Rundown.ContentsChanged += () =>
        {
            if (!loadingShow && settingsReady) dirty = true;
            QueueSettingsSave();
        };
        CueModePage.Rundown.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CueRundown.Next)) QueueSettingsSave();
        };
        CueModePage.Rundown.Restore(savedSettings.Rundown ?? [], savedSettings.NextRunCueId, resume: savedSettings.Rundown != null);
        RefreshCueModeState();
    }

    /// <summary>The cue page takes the transition and duration of the mixer for the cues it adds, and shows whether a projector is connected.</summary>
    private void RefreshCueModeState()
    {
        if (CueModePage == null) return;
        CueModePage.DefaultTransition = switcher.Style;
        CueModePage.DefaultDurationMilliseconds = switcher.DurationMilliseconds;
        CueModePage.SetOutputActive(output != null);
    }

    /// <summary>The rundown is running: cues are waiting, fading or queued for the program.</summary>
    private bool RundownBusy => CueModePage.Engine?.IsActive == true || switcher.IsTransitioning;
}
