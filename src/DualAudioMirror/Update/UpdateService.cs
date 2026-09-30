using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DualAudioMirror.Update
{
    public enum ChecksumStatus
    {
        Verified,
        Unavailable,
        Mismatch
    }

    public sealed class UpdateActionOutcome
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Warning { get; set; }
        public string FilePath { get; set; }
    }

    public static class UpdateService
    {
        private static readonly UpdateChecker Checker = new UpdateChecker();
        private static readonly HttpClient DownloadHttp = CreateDownloadHttp();
        private static bool _busy;
        private static bool _autoStarted;

        private static HttpClient CreateDownloadHttp()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DualAudioMirror/" + UpdateInfo.AppVersion);
            return client;
        }

        public static string PendingUpdatePath
        {
            get
            {
                try
                {
                    return AppSettings.Current.PendingUpdatePath ?? "";
                }
                catch (Exception)
                {
                    return "";
                }
            }
        }

        public static void StartAutoCheck()
        {
            try
            {
                if (_autoStarted) return;
                _autoStarted = true;
                _ = RunAutoCheckAsync();
            }
            catch (Exception ex)
            {
                Log("Falha ao agendar a verificação automática: " + ex.Message);
            }
        }

        private static async Task RunAutoCheckAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4));
                if (!AppSettings.Current.AutoCheckUpdates) return;
                if (_busy) return;
                string pending = AppSettings.Current.PendingUpdatePath;
                if (!string.IsNullOrEmpty(pending) && File.Exists(pending))
                {
                    ShowWindow(UpdateWindowMode.Pending, null);
                    return;
                }
                UpdateCheckResult result = await Checker.CheckAsync();
                if (result != null && result.UpdateAvailable && string.IsNullOrEmpty(result.Error))
                    ShowWindow(UpdateWindowMode.NewUpdate, result);
            }
            catch (Exception ex)
            {
                Log("Falha na verificação automática de atualização: " + ex.Message);
            }
        }

        private static void ShowWindow(UpdateWindowMode mode, UpdateCheckResult info)
        {
            try
            {
                Application app = Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (_busy) return;
                        Window owner = app.MainWindow;
                        if (owner == null || !owner.IsLoaded) return;
                        UpdateWindow window = new UpdateWindow(mode, info);
                        window.Owner = owner;
                        _busy = true;
                        window.Closed += (s, e) => _busy = false;
                        window.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        _busy = false;
                        Log("Falha ao abrir a janela de atualização: " + ex.Message);
                    }
                }));
            }
            catch (Exception ex)
            {
                Log("Falha ao agendar a janela de atualização: " + ex.Message);
            }
        }

        public static void ShowManualCheck(Window owner)
        {
            try
            {
                if (_busy) return;
                _busy = true;
                UpdateWindow window = new UpdateWindow(UpdateWindowMode.Checking, null);
                if (owner != null) window.Owner = owner;
                window.Closed += (s, e) => _busy = false;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _busy = false;
                Log("Falha ao abrir a verificação manual: " + ex.Message);
                ShowMessage(owner, "Não foi possível verificar: " + ex.Message, MessageBoxImage.Error);
            }
        }

        public static Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
        {
            return Checker.CheckAsync(ct);
        }

        public static async Task DownloadAsync(string url, string destino, IProgress<double> progress, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("O endereço do arquivo está vazio.");
            string dir = Path.GetDirectoryName(destino);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (HttpResponseMessage response = await DownloadHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("Falha no download (HTTP " + (int)response.StatusCode + ").");
                long total = response.Content.Headers.ContentLength ?? -1;
                if (total <= 0) progress?.Report(-1);
                using (Stream source = await response.Content.ReadAsStreamAsync(ct))
                using (FileStream target = new FileStream(destino, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920];
                    long copied = 0;
                    int lastPercent = -1;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await target.WriteAsync(buffer, 0, read, ct);
                        copied += read;
                        if (total > 0)
                        {
                            int percent = (int)(copied * 100.0 / total);
                            if (percent != lastPercent)
                            {
                                lastPercent = percent;
                                progress?.Report(percent);
                            }
                        }
                    }
                    progress?.Report(100);
                }
            }
        }

        public static async Task<ChecksumStatus> VerifyChecksumAsync(string filePath, string checksumsAssetUrl, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(checksumsAssetUrl)) return ChecksumStatus.Unavailable;
            string content;
            try
            {
                using (HttpResponseMessage response = await DownloadHttp.GetAsync(checksumsAssetUrl, ct))
                {
                    if (!response.IsSuccessStatusCode) return ChecksumStatus.Unavailable;
                    content = await response.Content.ReadAsStringAsync(ct);
                }
            }
            catch (Exception)
            {
                return ChecksumStatus.Unavailable;
            }

            string actual;
            try
            {
                actual = ComputeSha256(filePath);
            }
            catch (Exception)
            {
                return ChecksumStatus.Unavailable;
            }

            string fileName = Path.GetFileName(filePath);
            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                string name = parts[1];
                if (name.StartsWith("*")) name = name.Substring(1);
                if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) continue;
                return string.Equals(parts[0], actual, StringComparison.OrdinalIgnoreCase)
                    ? ChecksumStatus.Verified
                    : ChecksumStatus.Mismatch;
            }
            return ChecksumStatus.Unavailable;
        }

        public static async Task<UpdateActionOutcome> InstallNowAsync(UpdateCheckResult info, IProgress<double> progress, CancellationToken ct)
        {
            UpdateActionOutcome outcome = new UpdateActionOutcome();
            try
            {
                if (info == null || string.IsNullOrEmpty(info.InstallerAssetUrl))
                {
                    outcome.Error = "Não há instalador disponível para esta atualização.";
                    return outcome;
                }
                string version = string.IsNullOrEmpty(info.LatestVersion) ? "nova" : info.LatestVersion;
                string destino = Path.Combine(Path.GetTempPath(), "DualAudioMirror-Setup-" + version + ".exe");
                await DownloadAsync(info.InstallerAssetUrl, destino, progress, ct);
                ChecksumStatus status = await VerifyChecksumAsync(destino, info.ChecksumsAssetUrl, ct);
                if (status == ChecksumStatus.Mismatch)
                {
                    outcome.Error = "A verificação de integridade falhou: o arquivo baixado não corresponde ao checksum publicado.";
                    try { File.Delete(destino); } catch (Exception) { }
                    return outcome;
                }
                if (status == ChecksumStatus.Unavailable)
                    outcome.Warning = "Não foi possível verificar a integridade do arquivo (checksum indisponível).";
                ClearPendingSetting();
                LaunchInstaller(destino);
                outcome.FilePath = destino;
                outcome.Success = true;
                ScheduleShutdown();
            }
            catch (OperationCanceledException)
            {
                outcome.Error = "Download cancelado.";
            }
            catch (Exception ex)
            {
                outcome.Error = ex.Message;
            }
            return outcome;
        }

        public static async Task<UpdateActionOutcome> DownloadLaterAsync(UpdateCheckResult info, IProgress<double> progress, CancellationToken ct)
        {
            UpdateActionOutcome outcome = new UpdateActionOutcome();
            try
            {
                if (info == null || string.IsNullOrEmpty(info.InstallerAssetUrl))
                {
                    outcome.Error = "Não há instalador disponível para esta atualização.";
                    return outcome;
                }
                string version = string.IsNullOrEmpty(info.LatestVersion) ? "nova" : info.LatestVersion;
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string destino = Path.Combine(downloads, "DualAudioMirror-Setup-" + version + ".exe");
                await DownloadAsync(info.InstallerAssetUrl, destino, progress, ct);
                ChecksumStatus status = await VerifyChecksumAsync(destino, info.ChecksumsAssetUrl, ct);
                if (status == ChecksumStatus.Mismatch)
                {
                    outcome.Error = "A verificação de integridade falhou: o arquivo baixado não corresponde ao checksum publicado.";
                    try { File.Delete(destino); } catch (Exception) { }
                    return outcome;
                }
                if (status == ChecksumStatus.Unavailable)
                    outcome.Warning = "Não foi possível verificar a integridade do arquivo (checksum indisponível).";
                AppSettings.Current.PendingUpdatePath = destino;
                AppSettings.Save();
                outcome.FilePath = destino;
                outcome.Success = true;
            }
            catch (OperationCanceledException)
            {
                outcome.Error = "Download cancelado.";
            }
            catch (Exception ex)
            {
                outcome.Error = ex.Message;
            }
            return outcome;
        }

        public static UpdateActionOutcome InstallPending()
        {
            UpdateActionOutcome outcome = new UpdateActionOutcome();
            try
            {
                string path = AppSettings.Current.PendingUpdatePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    outcome.Error = "O arquivo baixado não foi encontrado neste computador.";
                    return outcome;
                }
                ClearPendingSetting();
                LaunchInstaller(path);
                outcome.FilePath = path;
                outcome.Success = true;
                ScheduleShutdown();
            }
            catch (Exception ex)
            {
                outcome.Error = ex.Message;
            }
            return outcome;
        }

        public static UpdateActionOutcome ClearPendingDownload()
        {
            UpdateActionOutcome outcome = new UpdateActionOutcome();
            try
            {
                string path = AppSettings.Current.PendingUpdatePath;
                if (!string.IsNullOrEmpty(path))
                {
                    if (File.Exists(path)) File.Delete(path);
                    AppSettings.Current.PendingUpdatePath = "";
                    AppSettings.Save();
                }
                outcome.Success = true;
            }
            catch (Exception ex)
            {
                outcome.Error = "Não foi possível excluir o arquivo: " + ex.Message;
            }
            return outcome;
        }

        public static void IgnoreVersion(string version)
        {
            try
            {
                AppSettings.Current.IgnoredVersion = version ?? "";
                AppSettings.Save();
            }
            catch (Exception ex)
            {
                Log("Falha ao ignorar a versão: " + ex.Message);
            }
        }

        private static void LaunchInstaller(string path)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = path;
            psi.Arguments = "/SILENT /SP- /NOCANCEL /NORESTART";
            psi.UseShellExecute = true;
            Process.Start(psi);
        }

        private static void ClearPendingSetting()
        {
            try
            {
                if (!string.IsNullOrEmpty(AppSettings.Current.PendingUpdatePath))
                {
                    AppSettings.Current.PendingUpdatePath = "";
                    AppSettings.Save();
                }
            }
            catch (Exception)
            {
            }
        }

        private static void ScheduleShutdown()
        {
            try
            {
                Application app = Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    DispatcherTimer timer = new DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(700);
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        try { app.Shutdown(); } catch (Exception) { }
                    };
                    timer.Start();
                }));
            }
            catch (Exception)
            {
            }
        }

        private static string ComputeSha256(string filePath)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static void ShowMessage(Window owner, string message, MessageBoxImage image)
        {
            try
            {
                if (owner != null)
                    MessageBox.Show(owner, message, "Verificar atualizações", MessageBoxButton.OK, image);
                else
                    MessageBox.Show(message, "Verificar atualizações", MessageBoxButton.OK, image);
            }
            catch (Exception)
            {
            }
        }

        private static void Log(string message)
        {
            try
            {
                Diag.Log("[Atualização] " + message);
            }
            catch (Exception)
            {
            }
        }
    }
}
