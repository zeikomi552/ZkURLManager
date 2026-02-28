using Prism.Ioc;
using Prism.Unity;
using System.Windows;
using Prism.Mvvm;

namespace ZkURLManager
{
    public partial class App : PrismApplication
    {
        protected override Window CreateShell()
        {   
            // コンテナから MainWindow を解決して起動する
            return ContainerLocator.Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 明示的に View と ViewModel を紐付け（重複定義が存在する場合に明確に指定）
            ViewModelLocationProvider.Register<MainWindow, ZkURLManager.ViewModels.MainWindowViewModel>();
        }
    }
}
