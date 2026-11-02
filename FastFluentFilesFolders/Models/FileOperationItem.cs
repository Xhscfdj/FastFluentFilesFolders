using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FastFluentFilesFolders.Models
{
    public enum FileOperationState
    {
        InProgress,
        Successful,
        Error,
        Canceled
    }

    public partial class FileOperationItem : INotifyPropertyChanged
    {
        private string _text = "";
        private double _progress;
        private int _fileCount;
        private string _process = "";
        private string _remainTime = "";
        private string _sizeText = "";
        private FileOperationState _state = FileOperationState.InProgress;
        private string _iconGlyph = "";
        private string _errorMessage = "";

        public string Text { get => _text; set { if (_text != value) { _text = value; Notify(); } } }
        public double Progress { get => _progress; set { if (_progress != value) { _progress = value; Notify(); } } }
        public int FileCount { get => _fileCount; set { if (_fileCount != value) { _fileCount = value; Notify(); Notify(nameof(ProgressDisplay)); } } }
        public string Process { get => _process; set { if (_process != value) { _process = value; Notify(); Notify(nameof(ProgressDisplay)); } } }
        public string RemainTime { get => _remainTime; set { if (_remainTime != value) { _remainTime = value; Notify(); } } }
        public string SizeText { get => _sizeText; set { if (_sizeText != value) { _sizeText = value; Notify(); Notify(nameof(ProgressDisplay)); } } }

        public string ProgressDisplay => string.Format(
            App.ML?.FileOpProgressFmt ?? "{0} items, progress {1}, size {2}",
            FileCount, Process, SizeText);

        public FileOperationState State { get => _state; set { if (_state != value) { _state = value; Notify(); Notify(nameof(DisplayIconGlyph)); Notify(nameof(IsCompleted)); Notify(nameof(IsInProgress)); Notify(nameof(IsError)); } } }
        public string IconGlyph { get => _iconGlyph; set { if (_iconGlyph != value) { _iconGlyph = value; Notify(); Notify(nameof(DisplayIconGlyph)); } } }
        public string DisplayIconGlyph => State switch { FileOperationState.Successful => "\uE73E", FileOperationState.Error => "\uE783", FileOperationState.Canceled => "\uE711", _ => IconGlyph };
        public bool IsCompleted => State != FileOperationState.InProgress;
        public bool IsInProgress => State == FileOperationState.InProgress;
        public bool IsError => State == FileOperationState.Error;
        /// <summary>失败时的具体错误信息（供操作岛展示，便于用户看到真正的原因）。</summary>
        public string ErrorMessage { get => _errorMessage; set { if (_errorMessage != value) { _errorMessage = value; Notify(); } } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
