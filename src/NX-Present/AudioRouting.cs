using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NXPresent;

public record AudioChoice(string Id, string Name) { public override string ToString() => Name; }
public record AudioJournal(string[] Original, string Selected);

public sealed class AudioRouting : IDisposable
{
    private readonly IDeviceEnumerator enumerator = (IDeviceEnumerator)new DeviceEnumerator();
    private static string JournalPath => Path.Combine(App.DataDirectory, "audio-restore.json");
    private AudioJournal? journal;
    public AudioRouting()
    {
        if (File.Exists(JournalPath))
        {
            journal = JsonSerializer.Deserialize<AudioJournal>(File.ReadAllText(JournalPath));
            Restore();
        }
    }
    public List<AudioChoice> Devices()
    {
        Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out var collection));
        var result = new List<AudioChoice>();
        try
        {
            Marshal.ThrowExceptionForHR(collection.GetCount(out uint count));
            for (uint i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(collection.Item(i, out var device));
                try
                {
                    Marshal.ThrowExceptionForHR(device.GetId(out string id));
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out var store));
                    try
                    {
                        var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                        Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
                        try { result.Add(new AudioChoice(id, Marshal.PtrToStringUni(value.Pointer) ?? id)); }
                        finally { PropVariantClear(ref value); }
                    }
                    finally { Marshal.ReleaseComObject(store); }
                }
                finally { Marshal.ReleaseComObject(device); }
            }
        }
        finally { Marshal.ReleaseComObject(collection); }
        return result.OrderBy(x => x.Name).ToList();
    }
    public string DefaultId(int role = 1)
    {
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, role, out var device));
        try { Marshal.ThrowExceptionForHR(device.GetId(out string id)); return id; }
        finally { Marshal.ReleaseComObject(device); }
    }
    public void Select(string id)
    {
        if (!Devices().Any(d => d.Id == id)) throw new InvalidOperationException("Das gewählte Audiogerät ist nicht mehr verbunden.");
        string[] original = journal?.Original ?? [DefaultId(0), DefaultId(1), DefaultId(2)];
        journal = new AudioJournal(original, id);
        // Write the recovery information before the first system change.
        File.WriteAllText(JournalPath + ".tmp", JsonSerializer.Serialize(journal));
        File.Move(JournalPath + ".tmp", JournalPath, true);
        var policy = (IPolicyConfig)new PolicyConfig();
        try { for (int role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role)); }
        catch { Restore(); throw; }
        finally { Marshal.ReleaseComObject(policy); }
    }
    public void Restore()
    {
        if (journal == null) return;
        var policy = (IPolicyConfig)new PolicyConfig();
        bool restored = true;
        try
        {
            var active = Devices().Select(x => x.Id).ToHashSet();
            for (int role = 0; role < 3; role++)
            {
                // Respect a later manual change made in Windows.
                try
                {
                    if (DefaultId(role) == journal.Selected && journal.Original[role] != journal.Selected)
                    {
                        if (active.Contains(journal.Original[role])) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(journal.Original[role], role));
                        else restored = false;
                    }
                }
                catch (Exception ex) { App.Log(ex); restored = false; }
            }
            if (restored) { File.Delete(JournalPath); journal = null; }
        }
        finally { Marshal.ReleaseComObject(policy); }
    }
    public void Dispose() { Restore(); Marshal.ReleaseComObject(enumerator); }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid FormatId; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    // Windows' PolicyConfig COM API is not a public compatibility contract.
    // All calls are isolated here, checked for errors, and covered by recovery.
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] private class PolicyConfig { }
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
    }
}
