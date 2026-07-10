using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.ComponentModel;
using System.Diagnostics;

namespace FastFluentFilesFolders.Views
{
    public sealed partial class MainWindowView : Window
    {
        private MainWindowViewModel VM => App.SharedViewModel;
        private string _currentPage = "explorer";

        public MainWindowView()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            VM.PropertyChanged += OnVMPropertyChanged;
        }

        private void OnVMPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsSettingsOpen))
            {
                if (VM.IsSettingsOpen)
                    NavigateTo("settings");
                else
                    NavigateTo("explorer");
            }
        }

        private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var item = args.SelectedItemContainer ?? args.SelectedItem as NavigationViewItem;
            if (item?.Tag is string tag)
            {
                NavigateTo(tag);
            }
        }

        private void OnSettingsNavTapped(object sender, TappedRoutedEventArgs e)
        {
            NavigateTo("settings");
        }

        private void NavigateTo(string pageTag)
        {
            if (_currentPage == pageTag) return;
            _currentPage = pageTag;

            Debug.WriteLine($"[MainWindowView] NavigateTo: {pageTag}");

            ExplorerArea.Visibility = pageTag == "explorer" ? Visibility.Visible : Visibility.Collapsed;
            PluginsArea.Visibility = pageTag == "plugins" ? Visibility.Visible : Visibility.Collapsed;
            SettingsStandaloneArea.Visibility = pageTag == "settings" ? Visibility.Visible : Visibility.Collapsed;

            if (pageTag == "explorer")
            {
                VM.IsSettingsOpen = false;
            }
            else if (pageTag == "plugins")
            {
                PluginsArea.RefreshAll();
            }

            UpdateNavSelection(pageTag);
        }

        private void UpdateNavSelection(string pageTag)
        {
            foreach (var mi in MainNav.MenuItems)
            {
                if (mi is NavigationViewItem navItem && navItem.Tag as string == pageTag)
                {
                    MainNav.SelectedItem = navItem;
                    return;
                }
            }
        }
    }
}
