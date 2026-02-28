using System.Windows;

namespace ZkURLManager
{
    /// <summary>
    /// メインウィンドウのコードビハインド。XAML が UI とバインディングを定義し、
    /// この部分クラスはコンポーネントを初期化するコンストラクタを含みます。
    /// 単純なウィンドウのため、追加のコードビハインドは不要です。
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// InitializeComponent は XAML をこの部分クラスに接続し、ビジュアルツリーを構築します。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}
