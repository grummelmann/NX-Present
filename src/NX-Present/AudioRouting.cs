using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NXPresent;

public record AudioChoice(string Id, string Name) { public override string ToString() => Name; }
public record AudioJournal(string[] Original, string Selected, string[]? Expected = null);

public sealed class AudioRouting : IDisposable
{
    private readonly IDeviceEnumerator enumerator = (IDeviceEnumerator)new DeviceEnumerator();
    private static string JournalPath => Path.Combine(App.DataDirectory, "audio-restore.json");
    private AudioJournal? journal;
    private bool disposed;
    public AudioRouting()
    {
        try
        {
            if (File.Exists(JournalPath))
            {
                try { journal = ReadJournal(JournalPath); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                {
                    App.Log(ex);
                    File.Move(JournalPath, JournalPath + ".invalid-" + Guid.NewGuid().ToString("N"));
                }
                Restore();
            }
        }
        catch { Marshal.ReleaseComObject(enumerator); throw; }
    }
    internal static AudioJournal ReadJournal(string path)
    {
        var data = JsonSerializer.Deserialize<AudioJournal>(File.ReadAllText(path));
        if (data == null || data.Original == null || data.Original.Length != 3 || data.Original.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(data.Selected) || data.Expected != null && (data.Expected.Length != 3 || data.Expected.Any(x => x == null)))
            throw new InvalidDataException("Die gespeicherte Audio-Wiederherstellung ist ungültig.");
        return data;
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
        var previous = journal;
        string[] before = [DefaultId(0), DefaultId(1), DefaultId(2)];
        string[] original = previous?.Original ?? before;
        journal = new AudioJournal(original, id);
        // Write the recovery information before the first system change.
        File.WriteAllText(JournalPath + ".tmp", JsonSerializer.Serialize(journal));
        File.Move(JournalPath + ".tmp", JournalPath, true);
        var policy = (IPolicyConfig)new PolicyConfig();
        try { for (int role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role)); }
        catch
        {
            // Roll back this attempted change, including a second selection in the same session.
            bool rolledBack = true;
            var expected = new string[3];
            for (int role = 0; role < 3; role++)
            {
                try
                {
                    if (DefaultId(role) == id && before[role] != id) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(before[role], role));
                    string current = DefaultId(role);
                    expected[role] = current == id ? id : previous != null && OwnsDefault(previous, role, current) ? current : "";
                    if (current != before[role]) rolledBack = false;
                }
                catch (Exception ex) { App.Log(ex); rolledBack = false; expected[role] = id; }
            }
            journal = rolledBack ? previous : new AudioJournal(original, id, expected);
            try
            {
                if (journal == null) File.Delete(JournalPath);
                else { File.WriteAllText(JournalPath + ".tmp", JsonSerializer.Serialize(journal)); File.Move(JournalPath + ".tmp", JournalPath, true); }
            }
            catch (Exception ex) { App.Log(ex); }
            throw;
        }
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
                    string current = DefaultId(role);
                    if (OwnsDefault(journal, role, current) && journal.Original[role] != current)
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
    internal static bool OwnsDefault(AudioJournal data, int role, string current) => current == (data.Expected?[role] ?? data.Selected);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Restore(); } finally { Marshal.ReleaseComObject(enumerator); }
    }

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
