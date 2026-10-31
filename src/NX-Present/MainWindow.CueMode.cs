using System;
using System.Threading.Tasks;
using System.Windows;

namespace NXPresent;

public partial class MainWindow
{
    private bool rundownBusy;
    private RunCue? pendingStep;

    private void InitializeCueMode()
    {
        CueModePage.Attach(switcher);
        CueModePage.SetSources(cues);
        CueModePage.GoRequested += async (_, _) => await RunNextCue();
        CueModePage.SaveRequested += (sender, _) => SaveShow(sender!, new RoutedEventArgs());
        CueModePage.OpenRequested += (sender, _) => OpenShow(sender!, new RoutedEventArgs());
        CueModePage.LibraryRequested += (_, _) => MixerNav.IsChecked = true;
        CueModePage.Rundown.ContentsChanged += () =>
        {
            if (!loadingShow && settingsReady) dirty = true;
            QueueSettingsSave();
        };
        CueModePage.Rundown.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(CueRundown.Next)) return;
            QueueSettingsSave();
            if (settingsReady && !rundownBusy && CueModePage.Visibility == Visibility.Visible) await PrepareRunCue();
        };
        CueModePage.Rundown.Restore(savedSettings.Rundown ?? [], savedSettings.NextRunCueId, resume: savedSettings.Rundown != null);
        RefreshCueModeState();
    }

    private void RefreshCueModeState()
    {
        if (CueModePage == null) return;
        CueModePage.DefaultTransition = switcher.Style;
        CueModePage.DefaultDurationMilliseconds = switcher.DurationMilliseconds;
        CueModePage.SetOutputState(output != null, rundownBusy || switcher.IsTransitioning, switcher.Program.Cue == Cue.Black ? null : switcher.Program.Cue.Name, switcher.BlackLevel >= 1);
    }

    /// <summary>Puts the cue that is next in the rundown into the preview.</summary>
    private async Task PrepareRunCue()
    {
        var step = CueModePage.Rundown.Next;
        if (step == null || switcher.IsTransitioning) return;
        await switcher.SetPreviewAsync(step.Cue);
    }

    /// <summary>GO: the next cue goes to the preview, then over to the program with its own transition.</summary>
    private async Task RunNextCue()
    {
        var step = CueModePage.Rundown.Next;
        if (step == null || rundownBusy || switcher.IsTransitioning) return;
        rundownBusy = true;
        RefreshCueModeState();
        try
        {
            if (!await switcher.SetPreviewAsync(step.Cue)) { Status("Der Cue ist nicht bereit: " + (switcher.Preview?.Error ?? "Quelle nicht verfügbar")); return; }
            pendingStep = step;
            if (!switcher.Auto(step.Transition, step.DurationMilliseconds)) pendingStep = null;
        }
        finally
        {
            // A running transition keeps the rundown locked until the take completes.
            if (!switcher.IsTransitioning) { pendingStep = null; rundownBusy = false; RefreshCueModeState(); }
        }
    }

    /// <summary>Called when a take has finished: a rundown step becomes live only after its transition succeeded.</summary>
    private void CompleteRundownStep(SourceFeed incoming)
    {
        var step = pendingStep;
        pendingStep = null;
        if (step != null && incoming.Cue == step.Cue) CueModePage.Rundown.MarkLive(step);
        else CueModePage.Rundown.ClearLive();
        rundownBusy = false;
        RefreshCueModeState();
    }
}
