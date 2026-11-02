using FastFluentFilesFolders.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FastFluentFilesFolders.Views
{
    /// <summary>
    /// “检查更新”弹窗。NetSparkle 只负责检查/下载/安装，界面在这里自己画：
    /// 检查中 → 有新版本（版本号 + 更新内容）→ 下载（进度条）→ 安装并重启。
    /// </summary>
    public sealed partial class UpdateDialog : ContentDialog
    {
        private enum DialogState
        {
            Checking,
            UpToDate,
            UpdateAvailable,
            Downloading,
            ReadyToInstall,
            Installing,
            Failed,
        }

        private readonly AutoUpdateService _service;
        private readonly SparkleUpdater _sparkle;

        private AppCastItem? _update;
        private string? _downloadedPath;
        private Func<Task>? _retryAction;
        private DialogState _state = DialogState.Checking;
        private bool _closed;
        private bool _closingForUpdate;

        public UpdateDialog(AutoUpdateService service)
        {
            _service = service;
            _sparkle = service.Sparkle;

            CornerRadius = new CornerRadius(topLeft: 8.0d, topRight: 8.0d, bottomRight: 8.0d, bottomLeft: 8.0d);

			InitializeComponent();
            ApplyLocalizedTexts();

            // 更新服务是单例，弹窗每次都会重新创建，务必在这里订阅、在 Closed 里退订
            _service.CloseRequested += OnCloseRequested;
            _sparkle.DownloadMadeProgress += OnDownloadMadeProgress;
            _sparkle.DownloadFinished += OnDownloadFinished;
            _sparkle.DownloadHadError += OnDownloadHadError;
            _sparkle.DownloadedFileIsCorrupt += OnDownloadedFileIsCorrupt;

            Opened += OnDialogOpened;
            Closed += OnDialogClosed;
        }

        private void ApplyLocalizedTexts()
        {
            Title = App.ML.Get("UpdateDialogTitle");
            CurrentVersionCaption.Text = App.ML.Get("UpdateCurrentVersion");
            LatestVersionCaption.Text = App.ML.Get("UpdateLatestVersion");
            ReleaseNotesCaption.Text = App.ML.Get("UpdateReleaseNotes");
            CloseButtonText = App.ML.Get("UpdateLater");
            CurrentVersionText.Text = _service.CurrentVersion;
            LatestVersionText.Text = "—";
            StatusText.Text = App.ML.Get("UpdateChecking");
        }

        // ======================= 弹窗生命周期 =======================

        private async void OnDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            await StartCheckAsync();
        }

        private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            _closed = true;

            _service.CloseRequested -= OnCloseRequested;
            _sparkle.DownloadMadeProgress -= OnDownloadMadeProgress;
            _sparkle.DownloadFinished -= OnDownloadFinished;
            _sparkle.DownloadHadError -= OnDownloadHadError;
            _sparkle.DownloadedFileIsCorrupt -= OnDownloadedFileIsCorrupt;
        }

        private void OnDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            if (_closingForUpdate) return;

            if (_state == DialogState.Downloading)
            {
                // 关掉弹窗就取消下载，别在后台偷偷下完
                _service.CancelDownload();
            }
            else if (_state == DialogState.Installing)
            {
                // 安装脚本正在等本进程退出，此时不能关闭
                args.Cancel = true;
            }
        }

        // ======================= 按钮 =======================

        private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                switch (_state)
                {
                    case DialogState.UpdateAvailable:
                        args.Cancel = true; // 保持弹窗打开，进入下载
                        await DownloadUpdateAsync();
                        break;

                    case DialogState.ReadyToInstall:
                        args.Cancel = true;
                        await InstallUpdateAsync();
                        break;

                    case DialogState.Failed:
                        args.Cancel = true;
                        var retry = _retryAction;
                        if (retry != null)
                            await retry();
                        break;

                    case DialogState.UpToDate:
                        // 没有可安装的东西，直接关闭
                        break;

                    default:
                        // 检查 / 下载 / 安装进行中：不允许关闭
                        args.Cancel = true;
                        break;
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // “跳过此版本”：后台自动检查时不再提示该版本
            if (_state == DialogState.UpdateAvailable)
                _service.SkipVersion(_update?.Version);
        }

        // ======================= 检查更新 =======================

        private async Task StartCheckAsync()
        {
            _update = null;
            _downloadedPath = null;
            _retryAction = null;
            _state = DialogState.Checking;

            ErrorBar.IsOpen = false;
            ReleaseNotesPanel.Visibility = Visibility.Collapsed;
            ReleaseNotesLinkButton.Visibility = Visibility.Collapsed;
            ReleaseNotesLinkButton.CornerRadius = new CornerRadius(4,4,4,4);
            DownloadPanel.Visibility = Visibility.Collapsed;
            DownloadBar.Value = 0;
            LatestVersionText.Text = "—";
            StatusText.Text = App.ML.Get("UpdateChecking");
            CloseButtonText = App.ML.Get("UpdateLater");
            PrimaryButtonText = "";
            SecondaryButtonText = "";
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;
            SetBusy(true);

            UpdateInfo? info = null;
            Exception? error = null;
            try
            {
                info = await _service.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                error = ex;
                Debug.WriteLine($"[UpdateDialog] 检查更新失败: {ex}");
            }

            if (_closed) return;
            SetBusy(false);

            if (info == null || info.Status == UpdateStatus.CouldNotDetermine)
            {
                _retryAction = StartCheckAsync;
                ShowFailure(App.ML.Get("UpdateCheckFailed"),
                    error?.Message ?? App.ML.Get("UpdateCheckFailedHint"));
                return;
            }

            if (info.Status == UpdateStatus.UpdateAvailable && info.Updates.Count > 0)
            {
                _update = info.Updates[0];
                _state = DialogState.UpdateAvailable;

                LatestVersionText.Text = string.IsNullOrWhiteSpace(_update.Version)
                    ? App.ML.Get("UpdateUnknownVersion")
                    : _update.Version!;
                StatusText.Text = string.Format(App.ML.Get("UpdateAvailableFmt"), _update.Version);
                ApplyReleaseNotes(_update);

                PrimaryButtonText = App.ML.Get("UpdateDownloadAndInstall");
                SecondaryButtonText = App.ML.Get("UpdateSkipVersion");
                IsPrimaryButtonEnabled = true;
                IsSecondaryButtonEnabled = true;
                return;
            }

            // UpdateNotAvailable（忽略跳过版本后不会再出现 UserSkipped）
            _state = DialogState.UpToDate;
            LatestVersionText.Text = _service.CurrentVersion;
            StatusText.Text = App.ML.Get("UpdateUpToDate");
            CloseButtonText = App.ML.Get("UpdateClose");
            PrimaryButtonText = "";
            SecondaryButtonText = "";
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;
        }

        private void ApplyReleaseNotes(AppCastItem item)
        {
            var notes = item.Description;
            if (string.IsNullOrWhiteSpace(notes))
                notes = item.Title;

            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(notes)
                ? App.ML.Get("UpdateNoReleaseNotes")
                : notes;

            if (!string.IsNullOrWhiteSpace(item.ReleaseNotesLink) &&
                Uri.TryCreate(item.ReleaseNotesLink, UriKind.Absolute, out var uri))
            {
                ReleaseNotesLinkButton.Content = App.ML.Get("UpdateReleaseNotesLink");
                ReleaseNotesLinkButton.NavigateUri = uri;
                ReleaseNotesLinkButton.Visibility = Visibility.Visible;
            }

            PublishDateText.Text = item.PublicationDate == default
                ? ""
                : string.Format(App.ML.Get("UpdatePublishDateFmt"), item.PublicationDate.ToLocalTime().ToString("yyyy-MM-dd"));

            ReleaseNotesPanel.Visibility = Visibility.Visible;
        }

        // ======================= 下载 / 安装 =======================

        private async Task DownloadUpdateAsync()
        {
            if (_update == null) return;

            if (string.IsNullOrWhiteSpace(_update.DownloadLink))
            {
                _retryAction = DownloadUpdateAsync;
                ShowFailure(App.ML.Get("UpdateDownloadFailed"), App.ML.Get("UpdateNoDownloadLink"));
                return;
            }

            _state = DialogState.Downloading;
            _retryAction = null;
            ErrorBar.IsOpen = false;
            DownloadPanel.Visibility = Visibility.Visible;
            DownloadBar.Value = 0;
            DownloadText.Text = string.Format(App.ML.Get("UpdateDownloadingFmt"), 0);
            StatusText.Text = App.ML.Get("UpdateDownloadingStatus");
            PrimaryButtonText = "";
            SecondaryButtonText = "";
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;
            SetBusy(true);

            try
            {
                // 只是“开始下载”，结果通过 DownloadFinished / DownloadHadError 事件回来
                await _service.DownloadUpdateAsync(_update);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateDialog] 开始下载失败: {ex}");
                FailDownload(ex.Message);
            }
        }

        private async Task InstallUpdateAsync()
        {
            if (_update == null || string.IsNullOrEmpty(_downloadedPath)) return;

            _state = DialogState.Installing;
            _retryAction = InstallUpdateAsync;
            ErrorBar.IsOpen = false;
            DownloadPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = App.ML.Get("UpdateInstalling");
            PrimaryButtonText = "";
            SecondaryButtonText = "";
            IsPrimaryButtonEnabled = false;
            IsSecondaryButtonEnabled = false;
            SetBusy(true);

            try
            {
                // 正常情况下 Sparkle 会启动安装脚本并触发 CloseRequested，让本进程退出
                await _service.InstallUpdateAsync(_update, _downloadedPath);
                if (_closed) return;
                ShowFailure(App.ML.Get("UpdateInstallFailed"), App.ML.Get("UpdateInstallFailedHint"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateDialog] 安装失败: {ex}");
                if (_closed) return;
                ShowFailure(App.ML.Get("UpdateInstallFailed"), ex.Message);
            }
        }

        private void OnDownloadMadeProgress(object sender, AppCastItem item, ItemDownloadProgressEventArgs args)
        {
            RunOnUi(() =>
            {
                if (_state != DialogState.Downloading) return;
                var percent = Math.Clamp(args.ProgressPercentage, 0, 100);
                DownloadBar.Value = percent;
                DownloadText.Text = string.Format(App.ML.Get("UpdateDownloadingFmt"), percent);
            });
        }

        private void OnDownloadFinished(AppCastItem item, string path)
        {
            RunOnUi(() =>
            {
                if (_state != DialogState.Downloading) return;

                _downloadedPath = path;
                _state = DialogState.ReadyToInstall;
                _retryAction = null;
                SetBusy(false);

                DownloadBar.Value = 100;
                DownloadText.Text = string.Format(App.ML.Get("UpdateDownloadingFmt"), 100);
                StatusText.Text = App.ML.Get("UpdateReadyToInstall");
                PrimaryButtonText = App.ML.Get("UpdateInstallNow");
                IsPrimaryButtonEnabled = true;
            });
        }

        private void OnDownloadHadError(AppCastItem item, string? path, Exception? exception)
        {
            RunOnUi(() => FailDownload(exception?.Message ?? App.ML.Get("UpdateDownloadFailed")));
        }

        private void OnDownloadedFileIsCorrupt(AppCastItem item, string path)
        {
            RunOnUi(() => FailDownload(App.ML.Get("UpdateSignatureInvalid")));
        }

        private void FailDownload(string message)
        {
            if (_state != DialogState.Downloading) return;

            _retryAction = DownloadUpdateAsync;
            DownloadPanel.Visibility = Visibility.Collapsed;
            ShowFailure(App.ML.Get("UpdateDownloadFailed"), message);
        }

        // ======================= 宿主退出（安装程序在等这个） =======================

        private void OnCloseRequested(object? sender, EventArgs e)
        {
            RunOnUi(() =>
            {
                if (_closed) return;

                _state = DialogState.Installing;
                _closingForUpdate = true;
                try { Hide(); } catch { }

                if (App.MainWindow != null)
                    App.MainWindow.Close();
                else
                    Application.Current.Exit();

                // 兜底：安装脚本只等 90 秒，万一还有前台线程拖着进程不退出就强杀，
                // 否则会出现“程序关了、更新没装上”的尴尬结果
                _ = Task.Delay(TimeSpan.FromSeconds(8))
                        .ContinueWith(_ => Environment.Exit(0));
            });
        }

        // ======================= 小工具 =======================

        private void ShowFailure(string status, string detail)
        {
            _state = DialogState.Failed;
            StatusText.Text = status;
            ErrorBar.Message = string.IsNullOrWhiteSpace(detail) ? status : detail;
            ErrorBar.IsOpen = true;
            CloseButtonText = App.ML.Get("UpdateClose");
            PrimaryButtonText = App.ML.Get("UpdateRetry");
            SecondaryButtonText = "";
            IsPrimaryButtonEnabled = true;
            IsSecondaryButtonEnabled = false;
            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            BusyRing.IsActive = busy;
            BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RunOnUi(Action action)
        {
            if (_closed) return;

            var queue = DispatcherQueue;
            if (queue == null || queue.HasThreadAccess)
                action();
            else
                queue.TryEnqueue(() => action());
        }
    }
}
