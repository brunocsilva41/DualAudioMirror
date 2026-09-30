using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;

namespace DualAudioMirror
{
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    internal class CPolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, bool bDefault, out IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr MixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, bool bDefault, out IntPtr pmftDefaultPeriod, out IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, out IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, out IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint(string wszDeviceId, int eRole);
        [PreserveSig] int SetEndpointVisibility(string wszDeviceId, bool bVisible);
    }

    public static class DevicePolicy
    {
        public static string GetDefaultDeviceId()
        {
            MMDeviceEnumerator enumerator = null;
            MMDevice device = null;
            try
            {
                enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return device.ID;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                DisposeQuietly(device);
                DisposeQuietly(enumerator);
            }
        }

        public static bool IsDefault(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return false;

            MMDeviceEnumerator enumerator = null;
            MMDevice device = null;
            try
            {
                enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return string.Equals(device.ID, deviceId, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                DisposeQuietly(device);
                DisposeQuietly(enumerator);
            }
        }

        public static bool TrySetDefault(string deviceId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                error = "Identificador do dispositivo inválido.";
                return false;
            }

            try
            {
                if (!DeviceExists(deviceId))
                {
                    error = "Dispositivo de áudio não encontrado.";
                    return false;
                }

                if (IsDefault(deviceId)) return true;

                DateTime start = DateTime.UtcNow;
                int lastHr = 0;
                while (true)
                {
                    lastHr = ApplyDefaultEndpoint(deviceId);
                    if (IsDefault(deviceId)) return true;
                    if ((DateTime.UtcNow - start).TotalMilliseconds >= 3000) break;
                    Thread.Sleep(150);
                }

                error = "Não foi possível tornar o dispositivo o padrão (HRESULT " + lastHr + ").";
                return false;
            }
            catch (Exception ex)
            {
                error = "Erro ao definir o dispositivo padrão: " + ex.Message;
                return false;
            }
        }

        public static string FindVirtualCableDeviceId()
        {
            return FindDeviceId(name =>
                name.IndexOf("CABLE INPUT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string FindDeviceIdByNameFragment(string fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment)) return null;
            return FindDeviceId(name =>
                name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FindDeviceId(Func<string, bool> match)
        {
            MMDeviceEnumerator enumerator = null;
            try
            {
                enumerator = new MMDeviceEnumerator();
                MMDeviceCollection collection =
                    enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                for (int i = 0; i < collection.Count; i++)
                {
                    MMDevice device = null;
                    try
                    {
                        device = collection[i];
                        string name = device.FriendlyName;
                        if (!string.IsNullOrEmpty(name) && match(name))
                            return device.ID;
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        DisposeQuietly(device);
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                DisposeQuietly(enumerator);
            }
        }

        private static int ApplyDefaultEndpoint(string deviceId)
        {
            object client = null;
            try
            {
                client = new CPolicyConfigClient();
                var policy = (IPolicyConfig)client;
                int hr = policy.SetDefaultEndpoint(deviceId, 0);
                policy.SetDefaultEndpoint(deviceId, 1);
                policy.SetDefaultEndpoint(deviceId, 2);
                return hr;
            }
            finally
            {
                if (client != null)
                {
                    try { Marshal.ReleaseComObject(client); }
                    catch (Exception) { }
                }
            }
        }

        private static bool DeviceExists(string deviceId)
        {
            MMDeviceEnumerator enumerator = null;
            MMDevice device = null;
            try
            {
                enumerator = new MMDeviceEnumerator();
                device = enumerator.GetDevice(deviceId);
                return device != null;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                DisposeQuietly(device);
                DisposeQuietly(enumerator);
            }
        }

        private static void DisposeQuietly(IDisposable value)
        {
            if (value == null) return;
            try { value.Dispose(); }
            catch (Exception) { }
        }
    }
}
