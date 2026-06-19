using CommunityToolkit.Mvvm.ComponentModel;
using LRS.Services;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;

namespace LRS.ViewModels
{
    public partial class FileNodeViewModel : FileSystemNodeViewModel
    {
        private readonly Configs _configs;
        [ObservableProperty] public ObservableCollection<FileSystemNodeViewModel> _childrens = [];
        [ObservableProperty] public bool _isLoaded = false;
        public string _formattedSize => FormatFileSize(ExactSize);

        public FileNodeViewModel(string fullPath, IIconProvider iconProvider, Configs configs, Microsoft.UI.Dispatching.DispatcherQueue uiDispatcherQueue) : base(fullPath, uiDispatcherQueue)
        {
            _configs = configs;
            if (iconProvider == null) throw new ArgumentNullException(nameof(iconProvider));
            Name = Path.GetFileName(fullPath);
            FullPath = fullPath;
            Extension = Path.GetExtension(fullPath);
            // 先设置 _iconProvider，然后再调用 LoadIconAsync
            IconProvider = iconProvider;
            //_ = LoadIconAsync(false, true, FullPath, uiDispatcherQueue);
            if (!(fullPath == $"{fullPath[0]}:\\") && File.Exists(fullPath))
            {
                FileInfo fileInfo = new(fullPath);
                ExactSize = fileInfo.Length;
                VisualSize = FormatFileSize(ExactSize);
                //Debug.WriteLine($"File: {fullPath}, Visual size: {VisualSize}, Exact size: {ExactSize}", "Visual size setted.");
                //Debug.WriteLine($"File: {fullPath}, Visual size: {VisualSize}, Exact size: {ExactSize}", "Visual size setted.");
                LastModifiedTime = fileInfo.LastWriteTimeUtc;
                FirstCreatedTime = fileInfo.CreationTimeUtc;
                LastModifiedTimeString = LastModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
                FirstCreatedTimeString = FirstCreatedTime.ToString("yyyy-MM-dd HH:mm:ss");
                Debug.WriteLine($"File: {fullPath} completely loaded.\nVisual size: {VisualSize}\nExact size: {ExactSize}\nLast modified: {LastModifiedTimeString}\nFirst created: {FirstCreatedTimeString}\nFile info loaded.");
            }
        }

    }
}