using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DualAudioMirror
{
    public sealed class ComboDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public sealed class TargetRow : INotifyPropertyChanged
    {
        private bool _selected;
        private string _delayText = "0";

        public string DeviceId { get; set; }
        public string Name { get; set; }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public string DelayText
        {
            get => _delayText;
            set
            {
                if (_delayText == value) return;
                _delayText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DelayText)));
            }
        }

        public int DelayMs
        {
            get
            {
                int v;
                if (!int.TryParse((_delayText ?? string.Empty).Trim(), out v)) v = 0;
                if (v < 0) v = 0;
                if (v > 400) v = 400;
                return v;
            }
        }

        public void NormalizeDelay()
        {
            DelayText = DelayMs.ToString();
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class MainWindow : Window
    {
        private sealed class TestFormat
        {
            public string Desc;
            public WaveFormat Format;
        }

        private static readonly TestFormat[] TestFormats =
        {
            new TestFormat { Desc = "48kHz float", Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) },
            new TestFormat { Desc = "44.1kHz float", Format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2) },
            new TestFormat { Desc = "48kHz PCM16", Format = new WaveFormat(48000, 16, 2) },
            new TestFormat { Desc = "44.1kHz PCM16", Format = new WaveFormat(44100, 16, 2) },
        };

        private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private readonly AudioEngine _engine = new AudioEngine();
        private readonly DispatcherTimer _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly ObservableCollection<ComboDevice> _allDevices = new ObservableCollection<ComboDevice>();
        private readonly ObservableCollection<TargetRow> _targets = new ObservableCollection<TargetRow>();

        private string _virtualCableId;
        private string _savedDefaultId;
        private bool _running;
        private bool _starting;
        private string _statusKind = "neutral";

        private bool SyncActive => ChkSync.IsChecked == true && !string.IsNullOrEmpty(_virtualCableId);

        public MainWindow()
        {
            AppSettings.Load();
            Themes.ThemeManager.Apply(AppSettings.Current.Theme);
            SourceInitialized += (s, e) => ApplyTitleBarTheme();
            InitializeComponent();
            double workHeight = SystemParameters.WorkArea.Height;
            if (Height > workHeight) Height = workHeight;
            if (MinHeight > workHeight) MinHeight = workHeight;
            DeviceCombo.ItemsSource = _allDevices;
            TargetList.ItemsSource = _targets;
            _ticker.Tick += (s, e) => RefreshDiagnostics();
            _engine.ErrorOccurred += OnEngineError;
            LogPathText.Text = "(abrindo log...)";
            try
            {
                LogPathText.Text = Diag.LogFilePath ?? "(log indisponível)";
            }
            catch (Exception)
            {
                LogPathText.Text = "(log indisponível)";
            }

            try
            {
                _virtualCableId = DevicePolicy.FindVirtualCableDeviceId();
            }
            catch (Exception)
            {
                _virtualCableId = null;
            }

            if (string.IsNullOrEmpty(_virtualCableId))
            {
                ChkSync.IsChecked = false;
                ChkSync.IsEnabled = false;
            }
            else
            {
                ChkSync.IsChecked = AppSettings.Current.LastSync;
            }

            RefreshDevices();

            string savedSourceId = AppSettings.Current.LastSourceDeviceId;
            if (!string.IsNullOrEmpty(savedSourceId) &&
                _allDevices.Any(d => d.Id == savedSourceId))
                DeviceCombo.SelectedValue = savedSourceId;

            RefreshLog();

            // Sem dispositivos, mantém o aviso já definido pelo RefreshDevices.
            if (_allDevices.Count == 0) { }
            else if (string.IsNullOrEmpty(_virtualCableId))
                SetStatus("Modo sincronizado indisponível: instale o \"VB-Cable\" (https://vb-audio.com/Cable/) e reabra o app para liberar a opção.", "error");
            else
                SetStatus(SyncActive
                    ? "Modo sincronizado: o cabo virtual é a fonte e todos os aparelhos marcados tocam junto."
                    : "Modo espelho: o principal é a fonte e apenas os aparelhos marcados tocam junto.",
                    "neutral");

            VersionText.Text = Update.UpdateInfo.AppVersion;
            BtnThemeToggle.Content = AppSettings.Current.Theme == "Light" ? "Tema: Claro" : "Tema: Escuro";
            RefreshStatsCard();
        }

        private static void Log(string message)
        {
            try
            {
                Diag.Log(message);
            }
            catch (Exception)
            {
            }
        }

        private void OnEngineError(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetStatus("Erro do motor: " + message, "error");
                Log("UI: erro do motor: " + message);
            }));
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_running || _starting) return;
            RefreshDevices();
        }

        private void RefreshDevices()
        {
            string preferredId = null;
            try
            {
                preferredId = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            }
            catch (Exception)
            {
            }

            _allDevices.Clear();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dev in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                string name = string.IsNullOrWhiteSpace(dev.FriendlyName) ? "(sem nome)" : dev.FriendlyName;
                if (!names.Add(name)) name += " [2]";
                _allDevices.Add(new ComboDevice { Id = dev.ID, Name = name });
            }

            if (preferredId != null)
                DeviceCombo.SelectedValue = preferredId;

            if (DeviceCombo.SelectedIndex < 0 && _allDevices.Count > 0)
                DeviceCombo.SelectedIndex = 0;

            RebuildTargets();

            if (_allDevices.Count == 0)
                SetStatus("Nenhum dispositivo de saída encontrado.", "error");
        }

        private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_running || _starting) return;
            RebuildTargets();
        }

        private void ChkSync_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized || StatusLine == null || TargetList == null) return;
            if (ChkSync.IsChecked == true && string.IsNullOrEmpty(_virtualCableId))
            {
                ChkSync.IsChecked = false;
                return;
            }
            if (_running) return;
            RebuildTargets();
            SetStatus(SyncActive
                ? "Modo sincronizado: o cabo virtual é a fonte e todos os aparelhos marcados tocam junto."
                : "Modo espelho: o principal é a fonte e apenas os aparelhos marcados tocam junto.",
                "neutral");
        }

        private static bool IsLikelyTvOrMonitor(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("tv") || n.Contains("monitor") || n.Contains("displayport") ||
                   n.Contains("hdmi") || n.Contains("tela") || n.Contains("screen") ||
                   n.Contains("dell") || n.Contains("samsung") || n.Contains("lg ");
        }

        private void RebuildTargets()
        {
            bool sync = SyncActive;
            string sourceId = sync
                ? _virtualCableId
                : (DeviceCombo.SelectedItem as ComboDevice)?.Id;

            var previous = new Dictionary<string, TargetRow>();
            foreach (var row in _targets)
                if (!previous.ContainsKey(row.DeviceId))
                    previous[row.DeviceId] = row;

            var saved = new Dictionary<string, DeviceSetting>();
            var savedList = AppSettings.Current.Targets;
            if (savedList != null)
            {
                foreach (var pref in savedList)
                    if (pref != null && !string.IsNullOrEmpty(pref.DeviceId) && !saved.ContainsKey(pref.DeviceId))
                        saved[pref.DeviceId] = pref;
            }

            _targets.Clear();
            foreach (var d in _allDevices)
            {
                if (!string.IsNullOrEmpty(sourceId) && d.Id == sourceId) continue;

                var row = new TargetRow { DeviceId = d.Id, Name = d.Name };
                TargetRow old;
                DeviceSetting pref;
                if (previous.TryGetValue(d.Id, out old))
                {
                    row.Selected = old.Selected;
                    row.DelayText = old.DelayText;
                }
                else if (saved.TryGetValue(d.Id, out pref))
                {
                    int delay = pref.DelayMs;
                    if (delay < 0) delay = 0;
                    if (delay > 400) delay = 400;
                    row.Selected = pref.Selected;
                    row.DelayText = delay.ToString();
                }
                else
                {
                    row.Selected = sync && IsLikelyTvOrMonitor(d.Name);
                }
                _targets.Add(row);
            }
        }

        private void SetRunningUi(bool running)
        {
            _running = running;
            DeviceCombo.IsEnabled = !running;
            BtnRefresh.IsEnabled = !running;
            ChkSetDefault.IsEnabled = !running;
            ChkSync.IsEnabled = !running && !string.IsNullOrEmpty(_virtualCableId);
            TargetList.IsEnabled = !running;
            BtnStart.IsEnabled = !running;
            BtnStop.IsEnabled = running;
            BtnTest.IsEnabled = !running;
        }

        private void SaveWindowsDefault(bool sync)
        {
            _savedDefaultId = null;
            if (!sync) return;
            try
            {
                _savedDefaultId = DevicePolicy.GetDefaultDeviceId();
            }
            catch (Exception)
            {
                _savedDefaultId = null;
            }
        }

        private void RestoreWindowsDefault()
        {
            if (string.IsNullOrEmpty(_savedDefaultId)) return;
            string id = _savedDefaultId;
            _savedDefaultId = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    string error;
                    if (DevicePolicy.TrySetDefault(id, out error))
                    {
                        Log("UI: padrão do Windows restaurado (tentativa " + attempt + ").");
                        return;
                    }
                    Log("UI: não foi possível restaurar o padrão do Windows (tentativa " + attempt + "): " + error);
                }
                catch (Exception ex)
                {
                    Log("UI: erro ao restaurar o padrão do Windows (tentativa " + attempt + "): " + ex.Message);
                }
                try
                {
                    if (DevicePolicy.IsDefault(id))
                    {
                        Log("UI: padrão do Windows restaurado (verificado na tentativa " + attempt + ").");
                        return;
                    }
                }
                catch (Exception)
                {
                }
                Thread.Sleep(400);
            }
            Log("UI: padrão do Windows NÃO pôde ser restaurado; restaure manualmente nas configurações de som.");
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_running || _starting) return;

            SavePreferences();

            bool sync = SyncActive;
            string sourceId;
            string sourceName;

            if (sync)
            {
                sourceId = _virtualCableId;
                sourceName = "cabo virtual (VB-Cable)";
            }
            else
            {
                var source = DeviceCombo.SelectedItem as ComboDevice;
                if (source == null)
                {
                    SetStatus("Selecione o dispositivo principal.", "error");
                    return;
                }
                sourceId = source.Id;
                sourceName = source.Name;
            }

            var rows = _targets.Where(r => r.Selected).ToList();
            if (rows.Count == 0)
            {
                SetStatus("Marque pelo menos um dispositivo para tocar junto.", "error");
                return;
            }

            try
            {
                _engine.Stop();
            }
            catch (Exception)
            {
            }
            RestoreWindowsDefault();

            SaveWindowsDefault(sync);

            var options = new AudioEngineOptions
            {
                SourceDeviceId = sourceId,
                Targets = rows.Select(r => new TargetSpec
                {
                    DeviceId = r.DeviceId,
                    Name = r.Name,
                    DelayMs = r.DelayMs
                }).ToList(),
                PrebufferMs = 60,
                CaptureBufferMs = 10,
                OutputLatencyMs = 20,
                SetDefaultToSource = sync || ChkSetDefault.IsChecked == true
            };

            _starting = true;
            BtnStart.IsEnabled = false;
            BtnStart.Content = "Iniciando...";
            BtnStop.IsEnabled = true;
            DeviceCombo.IsEnabled = false;
            BtnRefresh.IsEnabled = false;
            ChkSetDefault.IsEnabled = false;
            ChkSync.IsEnabled = false;
            TargetList.IsEnabled = false;
            SetStatus("Iniciando: aguardando pré-buffer de 60 ms" +
                      (sync ? " (reproduza algo; o som vai sair nos aparelhos marcados assim que encher)." :
                              " (reproduza algo no dispositivo principal)."),
                      "active");

            try
            {
                await Task.Run(() => _engine.Start(options));
            }
            catch (Exception ex)
            {
                try
                {
                    _engine.Stop();
                }
                catch (Exception)
                {
                }
                RestoreWindowsDefault();
                SetRunningUi(false);
                SetStatus("Erro ao iniciar: " + ex.Message, "error");
                Log("UI: falha ao iniciar: " + ex.Message);
                return;
            }
            finally
            {
                _starting = false;
                BtnStart.Content = "Iniciar";
            }

            EngineSnapshot snap = null;
            try
            {
                snap = _engine.GetSnapshot();
            }
            catch (Exception)
            {
            }

            if (snap == null || !snap.Running)
            {
                RestoreWindowsDefault();
                SetRunningUi(false);
                if (!string.Equals(StatusLine.Text, "Parado.", StringComparison.Ordinal))
                    SetStatus("Iniciamento cancelado.", "neutral");
                Log("UI: início cancelado pelo usuário.");
                return;
            }

            if (sync)
            {
                try
                {
                    if (!DevicePolicy.IsDefault(_virtualCableId))
                    {
                        string error;
                        if (!DevicePolicy.TrySetDefault(_virtualCableId, out error))
                            Log("UI: aviso, não foi possível definir o cabo como padrão: " + error);
                    }
                }
                catch (Exception ex)
                {
                    Log("UI: aviso ao definir padrão: " + ex.Message);
                }
            }

            SetRunningUi(true);
            _ticker.Start();
            RefreshDiagnostics();

            SetStatus((sync ? "Modo sincronizado ativo — fonte: " : "Espelhando: ") + sourceName +
                      " → " + string.Join(", ", rows.Select(r => r.Name)),
                      "active");
            Log("UI: iniciado, fonte=" + sourceName + ", alvos=" + rows.Count + ", sync=" + sync);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopEngine("Parado.");
        }

        private void StopEngine(string status)
        {
            _ticker.Stop();
            try
            {
                _engine.Stop();
            }
            catch (Exception ex)
            {
                Log("UI: erro ao parar: " + ex.Message);
            }
            RestoreWindowsDefault();
            SetRunningUi(false);
            StatsLine.Text = "";
            RefreshStatsCard();
            SetStatus(status, "neutral");
            Log("UI: parado.");
        }

        private void SetStatus(string message, string kind)
        {
            StatusLine.Text = message;
            _statusKind = kind == "active" || kind == "error" ? kind : "neutral";

            string dotKey = "Brush.TextSecondary";
            if (_statusKind == "active") dotKey = "Brush.Success";
            else if (_statusKind == "error") dotKey = "Brush.Error";

            string fgKey = _statusKind == "neutral" ? "Brush.TextSecondary" : "Brush.TextPrimary";

            try
            {
                var dot = Application.Current.FindResource(dotKey) as Brush;
                if (dot != null) StatusDot.Fill = dot;
                var fg = Application.Current.FindResource(fgKey) as Brush;
                if (fg != null) StatusLine.Foreground = fg;
            }
            catch (Exception)
            {
            }
        }

        private void SavePreferences()
        {
            try
            {
                AppSettings settings = AppSettings.Current;
                var source = DeviceCombo.SelectedItem as ComboDevice;
                settings.LastSourceDeviceId = source != null ? source.Id : "";
                settings.LastSync = ChkSync.IsChecked == true;
                if (settings.Targets == null)
                    settings.Targets = new List<DeviceSetting>();
                settings.Targets.Clear();
                foreach (var row in _targets)
                    settings.Targets.Add(new DeviceSetting
                    {
                        DeviceId = row.DeviceId,
                        Selected = row.Selected,
                        DelayMs = row.DelayMs
                    });
                AppSettings.Save();
            }
            catch (Exception)
            {
            }
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            string next = AppSettings.Current.Theme == "Light" ? "Dark" : "Light";
            Themes.ThemeManager.Apply(next);
            BtnThemeToggle.Content = next == "Light" ? "Tema: Claro" : "Tema: Escuro";
            SetStatus(StatusLine.Text, _statusKind);
            ApplyTitleBarTheme();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private void ApplyTitleBarTheme()
        {
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int dark = AppSettings.Current.Theme == "Dark" ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref dark, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref dark, 4);
            }
            catch (Exception)
            {
            }
        }

        private void RefreshStatsCard()
        {
            if (StatsCard == null || StatsLine == null) return;
            StatsCard.Visibility = string.IsNullOrWhiteSpace(StatsLine.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_running || _starting)
            {
                StatusLine.Text = "Pare a reprodução antes de verificar atualizações.";
                return;
            }

            Update.UpdateService.ShowManualCheck(this);
        }

        private void DelayBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            bool digits = !string.IsNullOrEmpty(e.Text);
            foreach (char c in e.Text)
                if (!char.IsDigit(c))
                    digits = false;
            e.Handled = !digits;
        }

        private void DelayBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            var row = box != null ? box.DataContext as TargetRow : null;
            if (row != null) row.NormalizeDelay();
        }

        private void LogPathText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            string path = LogPathText.Text;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    SetStatus("Não foi possível abrir a pasta do log: " + ex.Message, "error");
                }
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus("Não foi possível abrir o log: " + ex.Message, "error");
            }
        }

        private void RefreshDiagnostics()
        {
            EngineSnapshot snap;
            try
            {
                snap = _engine.GetSnapshot();
            }
            catch (Exception ex)
            {
                StatsLine.Text = "Erro ao ler estatísticas: " + ex.Message;
                RefreshStatsCard();
                return;
            }
            if (snap == null) return;

            var sb = new StringBuilder();
            sb.Append("Capturando: ").Append(snap.CapturedKbPerSec.ToString("F1"))
              .Append(" KB/s  |  mix: ").Append(string.IsNullOrEmpty(snap.MixFormat) ? "?" : snap.MixFormat);
            if (!string.IsNullOrEmpty(snap.SourceName))
                sb.Append("  |  fonte: ").Append(snap.SourceName);

            if (snap.Targets != null)
            {
                foreach (var t in snap.Targets)
                {
                    sb.AppendLine();
                    sb.Append("  ").Append(t.Name)
                      .Append(t.Alive ? " [ok]" : " [SEM SOM]")
                      .Append(" buf=").Append(t.BufferMs.ToString("F1"))
                      .Append("ms (alvo ").Append(t.TargetBufferMs.ToString("F0"))
                      .Append("ms) underruns=").Append(t.Underruns)
                      .Append(" overflow=").Append(t.Overflows)
                      .Append(" corr=").Append(t.DriftCorrections);
                    if (!t.Alive && !string.IsNullOrEmpty(t.LastError))
                        sb.Append(" — ").Append(t.LastError);
                }
            }

            if (!snap.HasCapturedAudio)
            {
                sb.AppendLine();
                sb.Append("  (aguardando áudio no dispositivo fonte...)");
            }

            if (!string.IsNullOrEmpty(snap.LastError))
            {
                sb.AppendLine();
                sb.Append("  Erro: ").Append(snap.LastError);
            }

            StatsLine.Text = sb.ToString();
            RefreshStatsCard();
            RefreshLog();
        }

        private void RefreshLog()
        {
            try
            {
                var lines = Diag.Recent(60);
                if (lines == null || lines.Count == 0) return;
                var text = string.Join(Environment.NewLine, lines);
                if (text == LogBox.Text) return;
                LogBox.Text = text;
                LogBox.ScrollToEnd();
            }
            catch (Exception)
            {
            }
        }

        private async void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            if (_running || _starting)
            {
                SetStatus("Clique em Parar antes de testar o som.", "error");
                return;
            }

            var rows = _targets.Where(r => r.Selected).ToList();
            if (rows.Count == 0)
            {
                SetStatus("Marque pelo menos um dispositivo para o teste.", "error");
                return;
            }

            BtnTest.IsEnabled = false;
            SetStatus("Testando... aguarde e OUÇA o tom de ~4 segundos.", "active");
            StatsLine.Text = "";
            RefreshStatsCard();

            try
            {
                string result = await Task.Run(() =>
                {
                    var lines = new List<string>();
                    foreach (var t in rows)
                    {
                        string used = "";
                        string err = "";
                        foreach (var f in TestFormats)
                        {
                            try
                            {
                                var mm = _enumerator.GetDevice(t.DeviceId);
                                var provider = new BufferedWaveProvider(f.Format)
                                {
                                    BufferDuration = TimeSpan.FromSeconds(6),
                                    DiscardOnBufferOverflow = true
                                };

                                byte[] tone = GenerateTone(f.Format, 4.0);
                                provider.AddSamples(tone, 0, tone.Length);

                                using (var outp = new WasapiOut(mm, AudioClientShareMode.Shared, true, 100))
                                {
                                    outp.Init(provider);
                                    outp.Play();
                                    Thread.Sleep(4200);
                                }

                                used = f.Desc;
                                break;
                            }
                            catch (Exception ex)
                            {
                                err = ex.Message;
                            }
                        }

                        lines.Add(used.Length > 0
                            ? "OK: " + t.Name + " (" + used + ")"
                            : "Falhou: " + t.Name + " -> " + err);
                    }
                    return string.Join(Environment.NewLine, lines);
                });

                SetStatus(result + Environment.NewLine +
                          "Você ouviu o tom? Se sim, a saída está OK. Agora clique em Iniciar e toque uma música.",
                          result.Contains("Falhou:") ? "error" : "neutral");
                Log("UI: teste concluído.");
            }
            catch (Exception ex)
            {
                SetStatus("Erro no teste: " + ex.Message, "error");
            }
            finally
            {
                BtnTest.IsEnabled = true;
            }
        }

        private static byte[] GenerateTone(WaveFormat format, double seconds)
        {
            double freq = 440.0;
            int frames = (int)(format.SampleRate * seconds);
            byte[] data = new byte[frames * format.BlockAlign];

            using (var ms = new MemoryStream(data))
            using (var w = new BinaryWriter(ms))
            {
                if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        float v = (float)(0.4 * Math.Sin(2 * Math.PI * freq * i / format.SampleRate));
                        for (int c = 0; c < format.Channels; c++) w.Write(v);
                    }
                }
                else
                {
                    for (int i = 0; i < frames; i++)
                    {
                        short v = (short)(0.4 * short.MaxValue * Math.Sin(2 * Math.PI * freq * i / format.SampleRate));
                        for (int c = 0; c < format.Channels; c++) w.Write(v);
                    }
                }
            }

            return data;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SavePreferences();
            _ticker.Stop();
            try
            {
                _engine.Stop();
            }
            catch (Exception)
            {
            }
            RestoreWindowsDefault();
            try
            {
                _engine.Dispose();
            }
            catch (Exception)
            {
            }
            try
            {
                _enumerator.Dispose();
            }
            catch (Exception)
            {
            }
            base.OnClosing(e);
        }
    }
}
