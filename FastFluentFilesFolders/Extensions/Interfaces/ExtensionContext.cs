using FastFluentFilesFolders.Services;
using FastFluentFilesFolders.ViewModels;
using Microsoft.UI.Dispatching;
using System;

namespace FastFluentFilesFolders.Extensions.Interfaces
{
    public class ExtensionContext
    {
        public IServiceProvider Services { get; }
        public DispatcherQueue UIDispatcherQueue { get; }
        public Configs AppConfigs { get; }
        public LocalizationService LocalizationService { get; }

        public ExtensionContext(
            IServiceProvider services,
            DispatcherQueue uiDispatcherQueue,
            Configs appConfigs,
            LocalizationService localizationService)
        {
            Services = services;
            UIDispatcherQueue = uiDispatcherQueue;
            AppConfigs = appConfigs;
            LocalizationService = localizationService;
        }

        public string GetString(string key) => LocalizationService.GetString(key);
    }
}
