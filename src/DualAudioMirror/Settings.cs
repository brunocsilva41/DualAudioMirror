using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DualAudioMirror
{
    public sealed class DeviceSetting
    {
        public string DeviceId { get; set; }
        public bool Selected { get; set; }
        public int DelayMs { get; set; }
    }

    public sealed class AppSettings
    {
        private static readonly object Sync = new object();
        private static AppSettings _current;
        private static bool _loaded;

        public string Theme { get; set; } = "Dark";
        public bool AutoCheckUpdates { get; set; } = true;
        public string IgnoredVersion { get; set; } = "";
        public string PendingUpdatePath { get; set; } = "";
        public string LastSourceDeviceId { get; set; } = "";
        public bool LastSync { get; set; } = true;
        public List<DeviceSetting> Targets { get; set; } = new List<DeviceSetting>();

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DualAudioMirror",
            "settings.json");

        public static AppSettings Current
        {
            get
            {
                lock (Sync)
                {
                    if (!_loaded) Load();
                    return _current;
                }
            }
        }

        public static void Load()
        {
            lock (Sync)
            {
                if (_loaded) return;
                try
                {
                    if (File.Exists(FilePath))
                        _current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                    else
                        _current = new AppSettings();
                }
                catch (Exception)
                {
                    _current = new AppSettings();
                }
                _loaded = true;
            }
        }

        public static void Save()
        {
            lock (Sync)
            {
                if (!_loaded || _current == null) return;
                try
                {
                    string dir = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(_current,
                        new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
