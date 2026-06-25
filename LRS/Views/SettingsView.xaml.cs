using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LRS.ViewModels;

namespace LRS.Views
{
    public sealed partial class SettingsView : Page
    {
        public SettingsView()
        {
            DataContext = App.SharedViewModel;
            InitializeComponent();
        }

        private void SaveSettings(object sender, RoutedEventArgs e)
        {
            App.SharedViewModel.AppConfigs.SaveConfig();
        }
    }
}
