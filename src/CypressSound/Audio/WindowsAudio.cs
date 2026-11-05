// Windows audio controls that have no public .NET API:
//  - IPolicyConfig: set the default output / input device (what the Sound settings page does).
//  - IAudioPolicyConfigFactory: send one app to a chosen device (Settings > System > Sound > Volume mixer).
// Both are undocumented COM interfaces that Windows has kept stable since Windows 7 / Windows 10 1803 and that
// tools such as SoundSwitch and EarTrumpet use.
using System.Runtime.InteropServices;

namespace CypressSound.Audio;

public enum DataFlow { Render = 0, Capture = 1 }
public enum Role { Console = 0, Multimedia = 1, Communications = 2 }

[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr format);
    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr defaultPeriod, IntPtr minPeriod);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
}

[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient { }

// Windows 10 21H2 and later (including every Windows 11 build). The first three methods are IInspectable's.
[ComImport, Guid("ab3d4648-e242-459f-b02f-541c70306324"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigFactory
{
    void GetIids(); void GetRuntimeClassName(); void GetTrustLevel();
    void add_CtxVolumeChange(); void remove_CtxVolumeChanged();
    void add_RingerVibrateStateChanged(); void remove_RingerVibrateStateChange();
    void SetVolumeGroupGainForId(); void GetVolumeGroupGainForId(); void GetActiveVolumeGroupForEndpointId(); void GetVolumeGroupsForEndpoint();
    void GetCurrentVolumeContext(); void SetVolumeGroupMuteForId(); void GetVolumeGroupMuteForId();
    void SetRingerVibrateState(); void GetRingerVibrateState();
    void SetPreferredChatApplication(); void ResetPreferredChatApplication(); void GetPreferredChatApplication(); void GetCurrentChatApplications();
    void add_ChatContextChanged(); void remove_ChatContextChanged();
    [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, DataFlow flow, Role role, IntPtr deviceId);
    [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, DataFlow flow, Role role, out IntPtr deviceId);
    [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
}

public static class WindowsAudio
{
    /// <summary>Make a device the Windows default for every role (games, music, calls).</summary>
    public static void SetDefault(string deviceId)
    {
        var pc = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            foreach (var role in new[] { Role.Console, Role.Multimedia, Role.Communications })
                Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(deviceId, role));
        }
        finally { Marshal.ReleaseComObject(pc); }
    }

    const string RenderInterface = "{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    const string CaptureInterface = "{2eef81be-33fa-4800-9670-1cd474972c3f}";

    [DllImport("combase.dll", PreserveSig = false)]
    static extern void RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", PreserveSig = false)]
    static extern void WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string src, int length, out IntPtr hstring);
    [DllImport("combase.dll", PreserveSig = false)]
    static extern void WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll")]
    static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    static IAudioPolicyConfigFactory? _factory;
    static IAudioPolicyConfigFactory Factory()
    {
        if (_factory != null) return _factory;
        const string cls = "Windows.Media.Internal.AudioPolicyConfig";
        WindowsCreateString(cls, cls.Length, out var h);
        try
        {
            var iid = typeof(IAudioPolicyConfigFactory).GUID;
            RoGetActivationFactory(h, ref iid, out var p);
            _factory = (IAudioPolicyConfigFactory)Marshal.GetObjectForIUnknown(p);
            Marshal.Release(p);
            return _factory;
        }
        finally { WindowsDeleteString(h); }
    }

    /// <summary>Send one app's sound to a device, or back to the Windows default when deviceId is null.</summary>
    public static void SetAppDevice(uint processId, string? deviceId, DataFlow flow = DataFlow.Render)
    {
        var f = Factory();
        IntPtr h = IntPtr.Zero;
        if (!string.IsNullOrEmpty(deviceId))
        {
            var full = $@"\\?\SWD#MMDEVAPI#{deviceId}#{(flow == DataFlow.Render ? RenderInterface : CaptureInterface)}";
            WindowsCreateString(full, full.Length, out h);
        }
        try
        {
            Marshal.ThrowExceptionForHR(f.SetPersistedDefaultAudioEndpoint(processId, flow, Role.Multimedia, h));
            Marshal.ThrowExceptionForHR(f.SetPersistedDefaultAudioEndpoint(processId, flow, Role.Console, h));
        }
        finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
    }

    /// <summary>The device an app was sent to, or null if it follows the Windows default.</summary>
    public static string? GetAppDevice(uint processId, DataFlow flow = DataFlow.Render)
    {
        try
        {
            if (Factory().GetPersistedDefaultAudioEndpoint(processId, flow, Role.Multimedia, out var h) != 0 || h == IntPtr.Zero) return null;
            try
            {
                var s = Marshal.PtrToStringUni(WindowsGetStringRawBuffer(h, out _)) ?? "";
                // \\?\SWD#MMDEVAPI#{0.0.0.00000000}.{guid}#{interface}
                var parts = s.Split('#');
                return parts.Length >= 3 ? parts[2] : null;
            }
            finally { WindowsDeleteString(h); }
        }
        catch { return null; }
    }
}
