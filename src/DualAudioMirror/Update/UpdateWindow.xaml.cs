using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DualAudioMirror.Update
{
    internal enum UpdateWindowMode
    {
        Checking,
        NewUpdate,
        Pending
    }

    public partial class UpdateWindow : Window
    {
        private UpdateWindowMode _mode;
        private UpdateCheckResult _info;
        private CancellationTokenSource _cts;
        private bool _downloading;
        private bool _checkCancelled;

        internal UpdateWindow(UpdateWindowMode mode, UpdateCheckResult info)
        {
            InitializeComponent();
            _mode = mode;
            _info = info;
            ApplyMode();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_mode == UpdateWindowMode.Checking)
                _ = RunCheckAsync();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_downloading)
            {
                e.Cancel = true;
                return;
            }
            if (_mode == UpdateWindowMode.Checking && !_checkCancelled)
            {
                _checkCancelled = true;
                try { _cts?.Cancel(); } catch (Exception) { }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try { _cts?.Dispose(); } catch (Exception) { }
            _cts = null;
        }

        private void ApplyMode()
        {
            switch (_mode)
            {
                case UpdateWindowMode.Checking:
                    TitleText.Text = "Verificando...";
                    SubtitleText.Text = "Aguarde um instante, procurando uma nova versão do DualAudioMirror.";
                    SetNotes("", false);
                    Progress.Visibility = Visibility.Collapsed;
                    BtnInstallNow.Content = "Atualizar agora";
                    BtnDownloadLater.Content = "Baixar e instalar depois";
                    BtnIgnore.Visibility = Visibility.Collapsed;
                    break;

                case UpdateWindowMode.NewUpdate:
                    TitleText.Text = "Atualização disponível";
                    SubtitleText.Text = "Versão atual " + UpdateInfo.AppVersion + " → nova " + LatestVersion();
                    string notes = _info != null ? _info.ReleaseNotes : null;
                    SetNotes(string.IsNullOrEmpty(notes) ? "Esta release não inclui notas de versão." : notes, true);
                    Progress.Visibility = Visibility.Collapsed;
                    BtnInstallNow.Content = "Atualizar agora";
                    BtnDownloadLater.Content = "Baixar e instalar depois";
                    BtnIgnore.Visibility = Visibility.Visible;
                    break;

                case UpdateWindowMode.Pending:
                    string path = UpdateService.PendingUpdatePath;
                    string pendingVersion = TryGetVersionFromFileName(path);
                    TitleText.Text = "Atualização baixada";
                    SubtitleText.Text = pendingVersion != null
                        ? "Versão atual " + UpdateInfo.AppVersion + " → nova " + pendingVersion
                        : "O instalador da nova versão já está baixado neste computador.";
                    SetNotes(string.IsNullOrEmpty(path)
                        ? "O arquivo baixado não foi encontrado."
                        : "Instalador salvo em:\n" + path, false);
                    Progress.Visibility = Visibility.Collapsed;
                    BtnInstallNow.Content = "Instalar agora";
                    BtnDownloadLater.Content = "Limpar download";
                    BtnIgnore.Visibility = Visibility.Collapsed;
                    break;
            }
            UpdateButtons();
        }

        private async Task RunCheckAsync()
        {
            if (_downloading) return;
            _cts = new CancellationTokenSource();
            try
            {
                UpdateCheckResult result = await UpdateService.CheckAsync(_cts.Token);
                if (_checkCancelled) return;
                if (result == null)
                    result = new UpdateCheckResult { Error = "Falha ao verificar as atualizações." };
                if (result.UpdateAvailable && string.IsNullOrEmpty(result.Error))
                {
                    _info = result;
                    _mode = UpdateWindowMode.NewUpdate;
                    ApplyMode();
                    return;
                }
                Window owner = Owner;
                Close();
                if (!string.IsNullOrEmpty(result.Error))
                    ShowOwnerMessage(owner, "Não foi possível verificar: " + result.Error, MessageBoxImage.Error);
                else
                    ShowOwnerMessage(owner, "Você está na última versão (" + UpdateInfo.AppVersion + ").", MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (_checkCancelled) return;
                Window owner = Owner;
                Close();
                ShowOwnerMessage(owner, "Não foi possível verificar: " + ex.Message, MessageBoxImage.Error);
            }
        }

        private void ShowOwnerMessage(Window owner, string message, MessageBoxImage image)
        {
            try
            {
                if (owner != null && owner.IsLoaded)
                    MessageBox.Show(owner, message, "Verificar atualizações", MessageBoxButton.OK, image);
                else
                    MessageBox.Show(message, "Verificar atualizações", MessageBoxButton.OK, image);
            }
            catch (Exception)
            {
            }
        }

        private async void BtnInstallNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_downloading) return;
                if (_mode == UpdateWindowMode.Pending)
                {
                    UpdateActionOutcome outcome = UpdateService.InstallPending();
                    if (outcome.Success)
                    {
                        _downloading = true;
                        UpdateButtons();
                        SetStatus(OutcomeStatus(outcome, "Iniciando a instalação..."), OutcomeBrush(outcome));
                    }
                    else
                    {
                        SetStatus(outcome.Error, "Brush.Error");
                    }
                    return;
                }
                BeginOperation();
                _cts = new CancellationTokenSource();
                Progress<double> progress = new Progress<double>(OnDownloadProgress);
                UpdateActionOutcome result = await UpdateService.InstallNowAsync(_info, progress, _cts.Token);
                if (result.Success)
                {
                    SetStatus(OutcomeStatus(result, "Iniciando a instalação..."), OutcomeBrush(result));
                }
                else
                {
                    EndOperation();
                    SetStatus(result.Error, "Brush.Error");
                }
            }
            catch (Exception ex)
            {
                EndOperation();
                SetStatus("Erro: " + ex.Message, "Brush.Error");
            }
        }

        private async void BtnDownloadLater_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_downloading) return;
                if (_mode == UpdateWindowMode.Pending)
                {
                    UpdateActionOutcome outcome = UpdateService.ClearPendingDownload();
                    if (outcome.Success)
                    {
                        Close();
                        return;
                    }
                    SetStatus(outcome.Error, "Brush.Error");
                    return;
                }
                BeginOperation();
                _cts = new CancellationTokenSource();
                Progress<double> progress = new Progress<double>(OnDownloadProgress);
                UpdateActionOutcome result = await UpdateService.DownloadLaterAsync(_info, progress, _cts.Token);
                EndOperation();
                if (result.Success)
                {
                    _mode = UpdateWindowMode.Pending;
                    ApplyMode();
                    SetStatus(OutcomeStatus(result, "Download salvo em:\n" + result.FilePath), OutcomeBrush(result));
                }
                else
                {
                    SetStatus(result.Error, "Brush.Error");
                }
            }
            catch (Exception ex)
            {
                EndOperation();
                SetStatus("Erro: " + ex.Message, "Brush.Error");
            }
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnIgnore_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string version = _info != null ? _info.LatestVersion : null;
                UpdateService.IgnoreVersion(version);
            }
            catch (Exception)
            {
            }
            Close();
        }

        private void BeginOperation()
        {
            _downloading = true;
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            Progress.Value = 0;
            UpdateButtons();
            SetStatus("Baixando a atualização...", "Brush.TextSecondary");
        }

        private void EndOperation()
        {
            _downloading = false;
            Progress.Visibility = Visibility.Collapsed;
            UpdateButtons();
        }

        private void OnDownloadProgress(double percent)
        {
            if (percent < 0) return;
            if (percent > 100) percent = 100;
            Progress.IsIndeterminate = false;
            Progress.Value = percent;
            SetStatus("Baixando a atualização... " + percent.ToString("0") + "%", "Brush.TextSecondary");
        }

        private void UpdateButtons()
        {
            bool pendingFile = _mode == UpdateWindowMode.Pending && PendingFileExists();
            BtnInstallNow.IsEnabled = !_downloading &&
                (_mode == UpdateWindowMode.NewUpdate || _mode == UpdateWindowMode.Pending);
            BtnDownloadLater.IsEnabled = !_downloading &&
                (_mode == UpdateWindowMode.NewUpdate || pendingFile);
            BtnLater.IsEnabled = !_downloading;
            BtnIgnore.IsEnabled = !_downloading && _mode == UpdateWindowMode.NewUpdate;
        }

        private void SetNotes(string text, bool showTitle)
        {
            NotesTitle.Visibility = showTitle ? Visibility.Visible : Visibility.Collapsed;
            if (string.IsNullOrEmpty(text))
            {
                NotesBorder.Visibility = Visibility.Collapsed;
                NotesText.Text = "";
            }
            else
            {
                NotesBorder.Visibility = Visibility.Visible;
                NotesText.Text = text;
            }
        }

        private void SetStatus(string text, string brushKey)
        {
            if (string.IsNullOrEmpty(text))
            {
                StatusText.Text = "";
                StatusText.Visibility = Visibility.Collapsed;
                return;
            }
            StatusText.Text = text;
            StatusText.Visibility = Visibility.Visible;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty,
                string.IsNullOrEmpty(brushKey) ? "Brush.TextSecondary" : brushKey);
        }

        private string LatestVersion()
        {
            if (_info != null && !string.IsNullOrEmpty(_info.LatestVersion)) return _info.LatestVersion;
            return "desconhecida";
        }

        private static bool PendingFileExists()
        {
            try
            {
                string path = UpdateService.PendingUpdatePath;
                return !string.IsNullOrEmpty(path) && File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string TryGetVersionFromFileName(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                string name = Path.GetFileNameWithoutExtension(path);
                const string marker = "Setup-";
                int index = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return null;
                string version = name.Substring(index + marker.Length);
                SemVer parsed;
                return SemVer.TryParse(version, out parsed) ? version : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string OutcomeStatus(UpdateActionOutcome outcome, string successText)
        {
            if (!string.IsNullOrEmpty(outcome.Warning)) return successText + "\n" + outcome.Warning;
            return successText;
        }

        private static string OutcomeBrush(UpdateActionOutcome outcome)
        {
            return string.IsNullOrEmpty(outcome.Warning) ? "Brush.Success" : "Brush.Warning";
        }
    }
}
