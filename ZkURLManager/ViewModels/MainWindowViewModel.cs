using Prism.Commands;
using Prism.Mvvm;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Diagnostics;
using System;
using System.Text;
using System.Windows;
using System.Xml.Serialization;
using System.IO;
using System.Collections.Generic;
using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZkURLManager.ViewModels
{
    /// <summary>
    /// アプリケーションのメインウィンドウ用の ViewModel。
    /// コマンドの実装と、URL パラメータ文字列やテンプレートを構築するためのキー/値エントリの一覧を保持します。
    /// プロパティ変更通知をサポートするため Prism の BindableBase を継承しています。
    /// </summary>
    public class MainWindowViewModel : BindableBase
    {
        // ウィンドウタイトルのバックフィールド
        private string _title = "ZkURLManager";

        /// <summary>
        /// UI に表示されるウィンドウタイトル。
        /// 値が変化したときにビューへ通知するため SetProperty を使用します。
        /// </summary>
        public string Title 
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        /// <summary>
        /// アプリケーションを終了するコマンド。
        /// </summary>
        public DelegateCommand ExitCommand { get; }
        /// <summary>
        /// パラメータやテンプレートを構築するためのキー/値エントリのコレクション。
        /// ビューが変更を監視できるよう ObservableCollection を使用しています。
        /// </summary>
        public ObservableCollection<Entry> Entries { get; } = new ObservableCollection<Entry>();
        private Entry _selectedEntry;
        public Entry SelectedEntry
        {
            get => _selectedEntry;
            set => SetProperty(ref _selectedEntry, value);
        }

        /// <summary>リストに新しいエントリを追加するコマンド。</summary>
        public DelegateCommand AddEntryCommand { get; }
        /// <summary>選択中のエントリを削除するコマンド。</summary>
        public DelegateCommand RemoveEntryCommand { get; }
        /// <summary>レンダリングされた URL をシステム既定のハンドラで開くコマンド。</summary>
        public DelegateCommand OpenUrlCommand { get; }
        /// <summary>現在の設定をファイルに保存するコマンド。</summary>
        public DelegateCommand SaveCommand { get; }
        /// <summary>ファイルから設定を読み込むコマンド。</summary>
        public DelegateCommand LoadCommand { get; }
        /// <summary>パラメータ文字列（key=value のペア）をクリップボードにコピーするコマンド。</summary>
        public DelegateCommand CopyParametersCommand { get; }
        /// <summary>パラメータテンプレート（プレースホルダ）をクリップボードにコピーするコマンド。</summary>
        public DelegateCommand CopyParameterTemplateCommand { get; }
        private string _urlTemplate = string.Empty;
        public string UrlTemplate
        {
            get => _urlTemplate;
            set
            {
                if (SetProperty(ref _urlTemplate, value))
                {
                    UpdateRenderedUrl();
                }
            }
        }

        private void OnCopyParameterTemplate()
        {
            // エントリのキーから "param1={param1}&param2={param2}" のようなテンプレート文字列を構築
            var sb = new StringBuilder();
            bool first = true;
            foreach (var e in Entries)
            {
                if (string.IsNullOrEmpty(e.Key))
                    continue;
                if (!first)
                    sb.Append('&');
                first = false;
                // テンプレート側のキーは URL エスケープしたものを使用し、プレースホルダは中括弧で囲んだ元のキーを使用する
                var encodedKey = Uri.EscapeDataString(e.Key);
                // 中括弧内はエスケープされていない元のキーをそのまま使用
                sb.Append(encodedKey).Append("={").Append(e.Key).Append("}");
            }

            var templateString = sb.ToString();
            try
            {
                Clipboard.SetText(templateString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"クリップボードにコピーできませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>エントリが少なくとも1件ある場合に true を返します（コピー可能かの判定）。</summary>
        private bool CanCopyParameters()
        {
            return Entries.Count > 0;
        }

        private void OnCopyParameters()
        {
            // エントリから "param1=value1&param2=value2" のような URL エンコード済みパラメータ文字列を構築
            var sb = new StringBuilder();
            bool first = true;
            foreach (var e in Entries)
            {
                if (string.IsNullOrEmpty(e.Key))
                    continue;
                if (!first)
                    sb.Append('&');
                first = false;
                var encodedKey = Uri.EscapeDataString(e.Key);
                var encodedValue = Uri.EscapeDataString(e.Value ?? string.Empty);
                sb.Append(encodedKey).Append('=').Append(encodedValue);
            }

            var paramString = sb.ToString();
            try
            {
                Clipboard.SetText(paramString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"クリップボードにコピーできませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // UI に表示される算出済み（レンダリング）URL のバックフィールド
        private string _renderedUrl = string.Empty;

        /// <summary>
        /// エントリをテンプレートに適用して生成される URL。
        /// テンプレートやエントリが変更されたときに再計算されます。
        /// </summary>
        public string RenderedUrl
        {
            get => _renderedUrl;
            private set => SetProperty(ref _renderedUrl, value);
        }

        /// <summary>
        /// コンストラクタ: コマンドの初期化、コレクション変更ハンドラの登録、サンプルデータの設定を行います。
        /// </summary>
        public MainWindowViewModel()
        {
            // コマンドとその CanExecute ロジックを初期化
            ExitCommand = new DelegateCommand(OnExit);
            AddEntryCommand = new DelegateCommand(OnAddEntry);
            RemoveEntryCommand = new DelegateCommand(OnRemoveEntry, CanRemoveEntry)
                .ObservesProperty(() => SelectedEntry);

            OpenUrlCommand = new DelegateCommand(OnOpenUrl, CanOpenUrl)
                .ObservesProperty(() => RenderedUrl);

            SaveCommand = new DelegateCommand(OnSave, CanSave)
                .ObservesProperty(() => UrlTemplate)
                .ObservesProperty(() => Entries.Count);

            LoadCommand = new DelegateCommand(OnLoad);
            CopyParametersCommand = new DelegateCommand(OnCopyParameters, CanCopyParameters)
                .ObservesProperty(() => Entries.Count);
            CopyParameterTemplateCommand = new DelegateCommand(OnCopyParameterTemplate, CanCopyParameters)
                .ObservesProperty(() => Entries.Count);

            // コレクションの変更を監視して、エントリの変更時にプレビューを再計算する
            Entries.CollectionChanged += Entries_CollectionChanged;

            // 初期表示用のサンプルデータ
            Entries.Add(new Entry { Key = "ExampleKey1", Value = "ExampleValue1" });
            Entries.Add(new Entry { Key = "ExampleKey2", Value = "ExampleValue2" });

            // レンダリングの例としてのデフォルトテンプレート
            UrlTemplate = "http://xxxx.xxx.xxx:?param1={ExampleKey1}&param2={ExampleKey2}";
        }

        /// <summary>アプリケーションを終了します。</summary>
        private void OnExit()
        {
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>空のエントリを追加して選択します。</summary>
        private void OnAddEntry()
        {
            var entry = new Entry { Key = "", Value = "" };
            Entries.Add(entry);
            SelectedEntry = entry;
        }

        /// <summary>選択中のエントリがあれば削除します。</summary>
        private void OnRemoveEntry()
        {
            if (SelectedEntry != null)
            {
                Entries.Remove(SelectedEntry);
                SelectedEntry = null;
            }
        }

        /// <summary>RemoveEntryCommand の CanExecute。エントリが選択されている場合のみ true を返します。</summary>
        private bool CanRemoveEntry()
        {
            return SelectedEntry != null;
        }

        /// <summary>
        /// Entries コレクションの変更ハンドラ。
        /// アイテムに対して PropertyChanged ハンドラを追加/削除して、各エントリの変更が URL の再レンダリングを引き起こすようにします。
        /// </summary>
        private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (Entry? oldItem in e.OldItems)
                {
                    if (oldItem != null)
                        oldItem.PropertyChanged -= Entry_PropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (Entry? newItem in e.NewItems)
                {
                    if (newItem != null)
                        newItem.PropertyChanged += Entry_PropertyChanged;
                }
            }

            // コレクションが変更されたらプレビューを再計算
            UpdateRenderedUrl();
        }

        /// <summary>
        /// エントリのプロパティが変更されたときに呼ばれます。レンダリングされた URL プレビューを更新します。
        /// </summary>
        private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // エントリに変更があればプレビューを更新します
            UpdateRenderedUrl();
        }

        /// <summary>
        /// UrlTemplate 内の {key} 形式のプレースホルダを対応するエントリ値で置換して、レンダリングされた URL を再計算します。
        /// 対応するエントリがない場合は空文字列に置換されます。
        /// </summary>
        private void UpdateRenderedUrl()
        {
            if (string.IsNullOrEmpty(UrlTemplate))
            {
                RenderedUrl = string.Empty;
                return;
            }

            // エントリからルックアップ辞書を構築。重複キーがある場合は後のエントリが優先されます。
            var lookup = Entries.Where(x => !string.IsNullOrEmpty(x.Key))
                                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

            // {key} のようなプレースホルダを対応する値で置換
            string result = Regex.Replace(UrlTemplate, "\\{([^}]+)\\}", match =>
            {
                var key = match.Groups[1].Value;
                if (lookup.TryGetValue(key, out var val))
                    return val ?? string.Empty;
                return string.Empty; // 見つからない場合は空文字列にする
            });

            RenderedUrl = result;
        }

        /// <summary>
        /// レンダリングされた URL をシステムの既定ハンドラで開こうとします。
        /// UseShellExecute = true を使用して OS による URI スキーム処理を行います。
        /// </summary>
        private void OnOpenUrl()
        {
            if (string.IsNullOrWhiteSpace(RenderedUrl))
                return;

            try
            {
                var psi = new ProcessStartInfo(RenderedUrl)
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"URL を開けませんでした: {ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>OpenUrlCommand の CanExecute。非空のレンダリング済み URL がある場合に true を返します。</summary>
        private bool CanOpenUrl()
        {
            return !string.IsNullOrWhiteSpace(RenderedUrl);
        }

        /// <summary>
        /// SaveCommand の CanExecute。テンプレートが存在するか、エントリが1件以上あれば保存を許可します。
        /// </summary>
        private bool CanSave()
        {
            // テンプレートがあるかエントリが存在する場合は保存を許可
            return !string.IsNullOrWhiteSpace(UrlTemplate) || Entries.Count > 0;
        }

        private void OnSave()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "ZK Style files (*.zkstyle)|*.zkstyle|All files (*.*)|*.*",
                DefaultExt = ".zkstyle",
                AddExtension = true,
                FileName = "settings.zkstyle"
            };

            if (dlg.ShowDialog() != true)
                return;

            var data = new SettingsData
            {
                UrlTemplate = this.UrlTemplate,
                Entries = this.Entries.Select(e => new SettingsEntry { Key = e.Key ?? string.Empty, Value = e.Value ?? string.Empty }).ToList()
            };

            try
            {
                using var fs = File.Create(dlg.FileName);
                var xs = new XmlSerializer(typeof(SettingsData));
                xs.Serialize(fs, data);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存できませんでした: {ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void OnLoad()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "ZK Style files (*.zkstyle)|*.zkstyle|All files (*.*)|*.*",
                DefaultExt = ".zkstyle"
            };

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                using var fs = File.OpenRead(dlg.FileName);
                var xs = new XmlSerializer(typeof(SettingsData));
                if (xs.Deserialize(fs) is SettingsData data)
                {
                    UrlTemplate = data.UrlTemplate ?? string.Empty;
                    Entries.Clear();
                    if (data.Entries != null)
                    {
                        foreach (var se in data.Entries)
                        {
                            Entries.Add(new Entry { Key = se.Key, Value = se.Value });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"読み込みに失敗しました: {ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 設定の保存/読み込み時に XML シリアル化で使用するコンテナクラス。
        /// </summary>
        [Serializable]
        public class SettingsData
        {
            public string UrlTemplate { get; set; } = string.Empty;
            public List<SettingsEntry> Entries { get; set; } = new List<SettingsEntry>();
        }

        /// <summary>
        /// SettingsData 内に格納される個々のエントリを表すクラス。
        /// </summary>
        [Serializable]
        public class SettingsEntry
        {
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }
    }

    public class Entry : INotifyPropertyChanged
    {
        private string _key;
        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); }
        }

        private string _value;
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}