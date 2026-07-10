using FastFluentFilesFolders.Extensions.Interfaces;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace FastFluentFilesFolders.Extensions.Extensions
{
    public class SamplePlugin : IContextMenuExtension, ISettingsExtension
    {
        private bool _isInitialized;
        public string Id => "FastFluentFilesFolders.SamplePlugin";
        public string DisplayName => "SamplePlugin.Name";
        public Version Version => new(1, 0, 0);

        private ICommand? _helloCommand;
        private ExtensionContext? _ctx;

        public Task InitializeAsync(ExtensionContext context)
        {
            _ctx = context;
            _isInitialized = true;
            Debug.WriteLine("[SamplePlugin] Initialized.");
            return Task.CompletedTask;
        }

        public Task ShutdownAsync()
        {
            _isInitialized = false;
            Debug.WriteLine("[SamplePlugin] Shutdown.");
            return Task.CompletedTask;
        }

        public IEnumerable<ExtensionMenuItem> GetMenuItems(
            FileSystemNodeViewModel? targetNode,
            ContextMenuLocation location)
        {
            if (!_isInitialized) yield break;

            if (location == ContextMenuLocation.FileItem)
            {
                yield return new ExtensionMenuItem { IsSeparator = true };
                yield return new ExtensionMenuItem
                {
                    Header = _ctx!.GetString("SamplePlugin.SayHelloFile"),
                    IconGlyph = "\uE8BD",
                    Command = _helloCommand ??= new RelayCommand(ShowHello),
                    CommandParameter = targetNode
                };
            }
            else if (location == ContextMenuLocation.Background)
            {
                yield return new ExtensionMenuItem { IsSeparator = true };
                yield return new ExtensionMenuItem
                {
                    Header = _ctx!.GetString("SamplePlugin.SayHelloFolder"),
                    IconGlyph = "\uE8BD",
                    Command = _helloCommand ??= new RelayCommand(ShowHello),
                };
            }
        }

        private void ShowHello()
        {
            Debug.WriteLine("[SamplePlugin] Hello from plugin!");
        }

        public IEnumerable<UIElement> CreateSettingsCards()
        {
            yield return new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = _ctx!.GetString("SamplePlugin.SettingsHeader"),
                HeaderIcon = new FontIcon { Glyph = "\uE946" },
                Description = _ctx!.GetString("SamplePlugin.SettingsDescription"),
                Content = new TextBlock { Text = $"v{Version}", VerticalAlignment = VerticalAlignment.Center }
            };
        }
    }
}
