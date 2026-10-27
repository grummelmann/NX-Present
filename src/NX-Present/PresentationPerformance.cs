using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NXPresent;

public sealed class PresentationPerformance : IDisposable
{
    private readonly Process process = Process.GetCurrentProcess();
    private readonly ProcessPriorityClass original;
    private bool changed;
    private bool disposed;
    public PresentationPerformance()
    {
        original = process.PriorityClass;
        try
        {
            if (original == ProcessPriorityClass.Normal || original == ProcessPriorityClass.BelowNormal || original == ProcessPriorityClass.Idle)
            {
                process.PriorityClass = ProcessPriorityClass.AboveNormal;
                changed = true;
            }
        }
        catch (Exception ex) { App.Log(ex); }
        SetThreadExecutionState(0x80000003);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SetThreadExecutionState(0x80000000);
        try { if (changed) process.PriorityClass = original; } catch (Exception ex) { App.Log(ex); }
        process.Dispose();
    }
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
}
