using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LRS.ViewModels;

namespace LRS.Views
{
    public sealed partial class SettingsView : Page
    {
        public SettingsViewModel SettingsViewModel = new();
        public SettingsView()
        {
            DataContext = new SettingsViewModel();
            InitializeComponent();
            OrderModeComboBox.DataContext = SettingsViewModel;
        }

        private void SaveSettings(object sender, RoutedEventArgs e)
        {
            App.SharedViewModel.AppConfigs.SaveConfig();
        }
        public void OnSettingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

	}
}
