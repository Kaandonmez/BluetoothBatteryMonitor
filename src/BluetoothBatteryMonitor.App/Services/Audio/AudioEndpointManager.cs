using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BluetoothBatteryMonitor.App.Models;
using Debug = global::System.Diagnostics.Debug;

namespace BluetoothBatteryMonitor.App.Services.Audio;

public class AudioEndpointManager : IAudioEndpointManager
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private static readonly Guid CLSID_PolicyConfigClient = new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
    private static readonly Guid CLSID_PolicyConfigVista = new("2946D359-4687-4BE1-B79F-21CE4B237F02");

    private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static readonly PROPERTYKEY PKEY_Device_DeviceDesc = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    private static readonly PROPERTYKEY PKEY_DeviceInterface_FriendlyName = new(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);
    private static readonly PROPERTYKEY PKEY_Device_InstanceId = new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);

    private const int eRender = 0;
    private const int eConsole = 0;
    private const int eMultimedia = 1;
    private const int eCommunications = 2;
    private const uint DEVICE_STATE_ACTIVE = 0x00000001;
    private const uint STGM_READ = 0x00000000;
    private const uint CLSCTX_INPROC_SERVER = 0x1;

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackEndpoints()
    {
        var list = new List<AudioDeviceInfo>();

        try
        {
            var enumerator = CreateEnumerator();
            if (enumerator == null) return list;

            string? defaultId = GetDefaultPlaybackDeviceId();

            int hr = enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out var collection);
            if (hr != 0 || collection == null) return list;

            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var dev) == 0 && dev != null)
                {
                    try
                    {
                        dev.GetId(out string id);
                        string name = GetDeviceProperty(dev, PKEY_Device_FriendlyName)
                                      ?? GetDeviceProperty(dev, PKEY_DeviceInterface_FriendlyName)
                                      ?? "Bilinmeyen Ses Cihazı";
                        string? desc = GetDeviceProperty(dev, PKEY_Device_DeviceDesc);
                        string? instanceId = GetDeviceProperty(dev, PKEY_Device_InstanceId);

                        bool isDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
                        var volInfo = GetVolumeFromDevice(dev);

                        list.Add(new AudioDeviceInfo(
                            Id: id,
                            Name: name,
                            Description: desc,
                            IsDefaultPlayback: isDefault,
                            VolumePercent: volInfo?.VolumePercent ?? 1.0f,
                            IsMuted: volInfo?.IsMuted ?? false,
                            DeviceInstanceId: instanceId
                        ));
                    }
                    catch
                    {
                        // Tekil cihaz okuma hatasını yut
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(dev);
                    }
                }
            }

            Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioEndpointManager] GetPlaybackEndpoints hatası: {ex.Message}");
        }

        return list;
    }

    public string? GetDefaultPlaybackDeviceId()
    {
        try
        {
            var enumerator = CreateEnumerator();
            if (enumerator == null) return null;

            int hr = enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var defaultDev);
            if (hr == 0 && defaultDev != null)
            {
                defaultDev.GetId(out string id);
                Marshal.ReleaseComObject(defaultDev);
                Marshal.ReleaseComObject(enumerator);
                return id;
            }

            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioEndpointManager] GetDefaultPlaybackDeviceId hatası: {ex.Message}");
        }

        return null;
    }

    public bool SetDefaultPlaybackDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;

        // 1. Windows 10/11 CPolicyConfigClient dene
        try
        {
            var policyConfig = new CPolicyConfigClient() as IPolicyConfig;
            if (policyConfig != null)
            {
                int hr1 = policyConfig.SetDefaultEndpoint(deviceId, eConsole);
                int hr2 = policyConfig.SetDefaultEndpoint(deviceId, eMultimedia);
                int hr3 = policyConfig.SetDefaultEndpoint(deviceId, eCommunications);
                Marshal.ReleaseComObject(policyConfig);
                return hr1 == 0 || hr2 == 0 || hr3 == 0;
            }
        }
        catch
        {
            // Vista fallback'e geç
        }

        // 2. Windows Vista/7/8 PolicyConfig dene
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_PolicyConfigVista);
            if (type != null)
            {
                var policyConfig = Activator.CreateInstance(type) as IPolicyConfigVista;
                if (policyConfig != null)
                {
                    int hr1 = policyConfig.SetDefaultEndpoint(deviceId, eConsole);
                    int hr2 = policyConfig.SetDefaultEndpoint(deviceId, eMultimedia);
                    int hr3 = policyConfig.SetDefaultEndpoint(deviceId, eCommunications);
                    Marshal.ReleaseComObject(policyConfig);
                    return hr1 == 0 || hr2 == 0 || hr3 == 0;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioEndpointManager] SetDefaultPlaybackDevice hatası: {ex.Message}");
        }

        return false;
    }

    public (float VolumePercent, bool IsMuted)? GetVolume(string? deviceId = null)
    {
        try
        {
            var enumerator = CreateEnumerator();
            if (enumerator == null) return null;

            IMMDevice? dev = null;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out dev);
            }
            else
            {
                enumerator.GetDevice(deviceId, out dev);
            }

            if (dev != null)
            {
                var result = GetVolumeFromDevice(dev);
                Marshal.ReleaseComObject(dev);
                Marshal.ReleaseComObject(enumerator);
                return result;
            }

            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioEndpointManager] GetVolume hatası: {ex.Message}");
        }

        return null;
    }

    public bool SetVolume(string? deviceId, float volumeScalar)
    {
        try
        {
            var enumerator = CreateEnumerator();
            if (enumerator == null) return false;

            IMMDevice? dev = null;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out dev);
            }
            else
            {
                enumerator.GetDevice(deviceId, out dev);
            }

            if (dev != null)
            {
                Guid iid = IID_IAudioEndpointVolume;
                int hr = dev.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out object objVol);
                if (hr == 0 && objVol is IAudioEndpointVolume epv)
                {
                    Guid empty = Guid.Empty;
                    float clamped = Math.Clamp(volumeScalar, 0.0f, 1.0f);
                    epv.SetMasterVolumeLevelScalar(clamped, ref empty);
                    Marshal.ReleaseComObject(epv);
                    Marshal.ReleaseComObject(dev);
                    Marshal.ReleaseComObject(enumerator);
                    return true;
                }
                Marshal.ReleaseComObject(dev);
            }

            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioEndpointManager] SetVolume hatası: {ex.Message}");
        }

        return false;
    }

    public bool SetMute(string? deviceId, bool isMuted)
    {
        try
        {
            var enumerator = CreateEnumerator();
            if (enumerator == null) return false;

            IMMDevice? dev = null;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out dev);
            }
            else
            {
                enumerator.GetDevice(deviceId, out dev);
            }

            if (dev != null)
            {
                Guid iid = IID_IAudioEndpointVolume;
                int hr = dev.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out object objVol);
                if (hr == 0 && objVol is IAudioEndpointVolume epv)
                {
                    Guid empty = Guid.Empty;
                    epv.SetMute(isMuted, ref empty);
                    Marshal.ReleaseComObject(epv);
                    Marshal.ReleaseComObject(dev);
                    Marshal.ReleaseComObject(enumerator);
                    return true;
                }
                Marshal.ReleaseComObject(dev);
            }

            Marshal.ReleaseComObject(enumerator);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioEndpointManager] SetMute hatası: {ex.Message}");
        }

        return false;
    }

    public AudioDeviceInfo? FindEndpointForBluetoothDevice(BluetoothDeviceModel device)
    {
        if (device == null) return null;

        var endpoints = GetPlaybackEndpoints();
        if (endpoints.Count == 0) return null;

        string cleanDevName = BluetoothDeviceModel.CleanNameForComparison(device.Name);

        // 1. MAC adresi eşleşmesi (DeviceInstanceId, Id veya Description içinde)
        if (device.BluetoothAddress != 0)
        {
            string macHex = device.BluetoothAddress.ToString("X12");
            var macMatch = endpoints.FirstOrDefault(e =>
                (!string.IsNullOrEmpty(e.DeviceInstanceId) && e.DeviceInstanceId.Contains(macHex, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.Id) && e.Id.Contains(macHex, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.Description) && e.Description.Contains(macHex, StringComparison.OrdinalIgnoreCase)));
            if (macMatch != null) return macMatch;
        }

        ulong extractedMac = BluetoothDeviceModel.ExtractMacAddress(device.Id);
        if (extractedMac != 0)
        {
            string macHex = extractedMac.ToString("X12");
            var macMatch = endpoints.FirstOrDefault(e =>
                (!string.IsNullOrEmpty(e.DeviceInstanceId) && e.DeviceInstanceId.Contains(macHex, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.Id) && e.Id.Contains(macHex, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.Description) && e.Description.Contains(macHex, StringComparison.OrdinalIgnoreCase)));
            if (macMatch != null) return macMatch;
        }

        // 2. Temizlenmiş ad tam eşleşmesi
        var exactMatch = endpoints.FirstOrDefault(e =>
        {
            string cleanEp = BluetoothDeviceModel.CleanNameForComparison(e.Name);
            return string.Equals(cleanEp, cleanDevName, StringComparison.OrdinalIgnoreCase);
        });
        if (exactMatch != null) return exactMatch;

        // 3. İsim içerme eşleşmesi
        if (!string.IsNullOrEmpty(cleanDevName) && cleanDevName.Length >= 3)
        {
            var containsMatch = endpoints.FirstOrDefault(e =>
            {
                string cleanEp = BluetoothDeviceModel.CleanNameForComparison(e.Name);
                return cleanEp.Contains(cleanDevName, StringComparison.OrdinalIgnoreCase) ||
                       cleanDevName.Contains(cleanEp, StringComparison.OrdinalIgnoreCase);
            });
            if (containsMatch != null) return containsMatch;
        }

        // 4. ModelName eşleşmesi
        if (!string.IsNullOrEmpty(device.ModelName) && device.ModelName.Length >= 3)
        {
            string cleanModel = BluetoothDeviceModel.CleanNameForComparison(device.ModelName);
            var modelMatch = endpoints.FirstOrDefault(e =>
            {
                string cleanEp = BluetoothDeviceModel.CleanNameForComparison(e.Name);
                return cleanEp.Contains(cleanModel, StringComparison.OrdinalIgnoreCase) ||
                       cleanModel.Contains(cleanEp, StringComparison.OrdinalIgnoreCase);
            });
            if (modelMatch != null) return modelMatch;
        }

        // 5. Kelime bazlı benzerlik (Örn: "Sony WH-1000XM4" -> "WH-1000XM4")
        var words = cleanDevName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(w => w.Length >= 3 && !BluetoothDeviceModel.IsGenericName(w))
                                .ToList();
        if (words.Count > 0)
        {
            var wordMatch = endpoints.FirstOrDefault(e =>
            {
                string cleanEp = BluetoothDeviceModel.CleanNameForComparison(e.Name);
                return words.Any(w => cleanEp.Contains(w, StringComparison.OrdinalIgnoreCase));
            });
            if (wordMatch != null) return wordMatch;
        }

        return null;
    }

    private static (float VolumePercent, bool IsMuted)? GetVolumeFromDevice(IMMDevice dev)
    {
        try
        {
            Guid iid = IID_IAudioEndpointVolume;
            int hr = dev.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out object objVol);
            if (hr == 0 && objVol is IAudioEndpointVolume epv)
            {
                epv.GetMasterVolumeLevelScalar(out float level);
                epv.GetMute(out bool mute);
                Marshal.ReleaseComObject(epv);
                return (level, mute);
            }
        }
        catch
        {
            // COM aktivasyon hatasını yut
        }
        return null;
    }

    private static string? GetDeviceProperty(IMMDevice dev, PROPERTYKEY key)
    {
        try
        {
            int hr = dev.OpenPropertyStore(STGM_READ, out var store);
            if (hr == 0 && store != null)
            {
                PROPVARIANT pv = default;
                if (store.GetValue(ref key, out pv) == 0)
                {
                    string? val = pv.GetValue();
                    PropVariantClear(ref pv);
                    Marshal.ReleaseComObject(store);
                    return val;
                }
                Marshal.ReleaseComObject(store);
            }
        }
        catch
        {
            // COM mülk okuma hatasını yut
        }
        return null;
    }

    private static IMMDeviceEnumerator? CreateEnumerator()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_MMDeviceEnumerator);
            if (type == null) return null;
            return Activator.CreateInstance(type) as IMMDeviceEnumerator;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        // Gerekirse global kaynakları temizle
    }

    #region COM P/Invoke & Interfaces

    [DllImport("Ole32.dll", PreserveSig = false)]
    private static extern void PropVariantClear(ref PROPVARIANT pvar);

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint pcDevices);
        [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;

        public PROPERTYKEY(Guid guid, uint id)
        {
            fmtid = guid;
            pid = id;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public IntPtr pwszVal;

        public string? GetValue()
        {
            if (vt == 31 && pwszVal != IntPtr.Zero) // VT_LPWSTR
            {
                return Marshal.PtrToStringUni(pwszVal);
            }
            return null;
        }
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig] int VolumeStepUp(ref Guid pguidEventContext);
        [PreserveSig] int VolumeStepDown(ref Guid pguidEventContext);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, int bDefault, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, ref PROPERTYKEY pKey, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, ref PROPERTYKEY pKey, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int eRole);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceName, int bVisible);
    }

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    internal class CPolicyConfigClient
    {
    }

    [ComImport]
    [Guid("568b9108-9a01-4810-8023-b321aa706958")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfigVista
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, int bDefault, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, ref PROPERTYKEY pKey, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, ref PROPERTYKEY pKey, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, int eRole);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceName, int bVisible);
    }

    #endregion
}
