using LRS.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace LRS.Views
{
    public sealed partial class MainWindowView : Window
    {
        private MainWindowViewModel VM => App.SharedViewModel;

        public MainWindowView()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            VM.PropertyChanged += OnVMPropertyChanged;
            UpdateRightPanel();
        }

        private void OnVMPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsSettingsOpen))
                UpdateRightPanel();
        }

        private void UpdateRightPanel()
        {
            if (VM.IsSettingsOpen)
            {
                MiddleFilesPanel.Visibility = Visibility.Collapsed;
                SettingsPanelView.Visibility = Visibility.Visible;
            }
            else
            {
                MiddleFilesPanel.Visibility = Visibility.Visible;
                SettingsPanelView.Visibility = Visibility.Collapsed;
            }
        }
    }
}
