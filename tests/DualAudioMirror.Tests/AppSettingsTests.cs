using System.Text.Json;
using Xunit;

namespace DualAudioMirror.Tests
{
    public class AppSettingsTests
    {
        [Fact]
        public void NewInstance_HasExpectedDefaults()
        {
            var settings = new AppSettings();

            Assert.Equal("Dark", settings.Theme);
            Assert.True(settings.AutoCheckUpdates);
            Assert.Equal("", settings.IgnoredVersion);
            Assert.Equal("", settings.PendingUpdatePath);
            Assert.Equal("", settings.LastSourceDeviceId);
            Assert.True(settings.LastSync);
            Assert.NotNull(settings.Targets);
            Assert.Empty(settings.Targets);
        }

        [Fact]
        public void SerializeThenDeserialize_RoundtripsAllProperties()
        {
            var original = new AppSettings
            {
                Theme = "Light",
                AutoCheckUpdates = false,
                IgnoredVersion = "1.2.3",
                PendingUpdatePath = @"C:\Temp\setup.exe",
                LastSourceDeviceId = "device-42",
                LastSync = false,
                Targets =
                {
                    new DeviceSetting { DeviceId = "a", Selected = true, DelayMs = 150 },
                    new DeviceSetting { DeviceId = "b", Selected = false, DelayMs = 400 },
                }
            };

            string json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<AppSettings>(json);

            Assert.NotNull(restored);
            Assert.Equal(original.Theme, restored.Theme);
            Assert.Equal(original.AutoCheckUpdates, restored.AutoCheckUpdates);
            Assert.Equal(original.IgnoredVersion, restored.IgnoredVersion);
            Assert.Equal(original.PendingUpdatePath, restored.PendingUpdatePath);
            Assert.Equal(original.LastSourceDeviceId, restored.LastSourceDeviceId);
            Assert.Equal(original.LastSync, restored.LastSync);
            Assert.Equal(2, restored.Targets.Count);
            Assert.Equal("a", restored.Targets[0].DeviceId);
            Assert.True(restored.Targets[0].Selected);
            Assert.Equal(150, restored.Targets[0].DelayMs);
            Assert.Equal("b", restored.Targets[1].DeviceId);
            Assert.False(restored.Targets[1].Selected);
            Assert.Equal(400, restored.Targets[1].DelayMs);
        }

        [Fact]
        public void Deserialize_EmptyObject_FallsBackToDefaults()
        {
            var restored = JsonSerializer.Deserialize<AppSettings>("{}");

            Assert.NotNull(restored);
            Assert.Equal("Dark", restored.Theme);
            Assert.True(restored.AutoCheckUpdates);
            Assert.True(restored.LastSync);
            Assert.NotNull(restored.Targets);
            Assert.Empty(restored.Targets);
        }

        [Fact]
        public void FilePath_IsUnderLocalAppDataDualAudioMirror()
        {
            Assert.EndsWith(System.IO.Path.Combine("DualAudioMirror", "settings.json"), AppSettings.FilePath);
        }
    }
}
