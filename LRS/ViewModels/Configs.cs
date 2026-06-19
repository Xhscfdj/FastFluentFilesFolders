using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LRS.ViewModels
{
    public partial class Configs : ObservableObject
    {
        public IConfiguration configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("./Configs/appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        // 所有配置
        private int _middleFilesHeight = 40;
        public int MiddleFilesHeight
        {
            get => _middleFilesHeight;
            set => _middleFilesHeight = value;
        }
        public bool ifUsesWin32APIToGetIcon;
        // 其他
        public Configs()
        {
            ReadConfigs();
            Debug.WriteLine($"Configs initialized. MiddleFilesHeight: {MiddleFilesHeight}");
            ChangeToken.OnChange(
                () => configuration.GetReloadToken(),
                () =>
                {
                    ReadConfigs();
                });

        }
        public void ReadConfigs()
        {
            MiddleFilesHeight = configuration.GetValue<int>("AppearanceSettings:MiddleFilesHeight");
            ifUsesWin32APIToGetIcon = configuration.GetValue<bool>("Advanced:UsesWin32APIToGetIcon");

            
        }

    }
}
