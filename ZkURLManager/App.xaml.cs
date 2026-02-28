using Prism.Ioc;
using Prism.Unity;
using System.Windows;
using Prism.Mvvm;

namespace ZkURLManager
{
    /// <summary>
    /// Prism によって管理されるアプリケーションのエントリポイント。
    /// PrismApplication は DI コンテナとの統合やアプリケーションのライフサイクルフックを提供します。
    /// </summary>
    public partial class App : PrismApplication
    {
        /// <summary>
        /// CreateShell は Prism によってアプリケーションのメインウィンドウを取得するために呼ばれます。
        /// ここでは DI コンテナから MainWindow を解決し、依存性注入を受けられるようにします（このプロジェクトでは Unity を使用）。
        /// </summary>
        protected override Window CreateShell()
        {   
            // コンテナから MainWindow を解決し、それをシェルとして返します
            return ContainerLocator.Container.Resolve<MainWindow>();
        }

        /// <summary>
        /// RegisterTypes は型のマッピングやサービス登録を行うために使用されます。
        /// また View と ViewModel の関連付けを登録し、Prism の ViewModelLocationProvider が自動的に紐付けられるようにします。
        /// </summary>
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 明示的に MainWindow とその ViewModel の関連付けを登録します。
            ViewModelLocationProvider.Register<MainWindow, ZkURLManager.ViewModels.MainWindowViewModel>();
        }
    }
}
