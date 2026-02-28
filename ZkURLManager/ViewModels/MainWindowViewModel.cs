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
        /// <summary>
        /// URL テンプレートとエントリ群をまとめたプリセットを表すクラス。
        /// </summary>
        public class Preset : INotifyPropertyChanged
        {
            private string _name = string.Empty;
            private string _urlTemplate = string.Empty;
            private string _icon = "Link";
            private string _description = string.Empty;

            public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
            public string UrlTemplate { get => _urlTemplate; set { _urlTemplate = value; OnPropertyChanged(); } }
            // 表示用のアイコン名（MaterialDesign PackIcon の Kind に対応）
            public string Icon { get => _icon; set { _icon = value; OnPropertyChanged(); } }
            // プリセットの説明文
            public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
            public ObservableCollection<Entry> Entries { get; set; } = new ObservableCollection<Entry>();

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // テーマ切替用プロパティ
        public ObservableCollection<string> AvailableThemes { get; } = new ObservableCollection<string> { "Light", "Dark" };

        private string _selectedTheme = "Light";
        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (SetProperty(ref _selectedTheme, value))
                {
                    ApplyTheme(value);
                }
            }
        }

        private void ApplyTheme(string name)
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                    return;

                var md = app.Resources.MergedDictionaries;
                // remove existing overlays
                for (int i = md.Count - 1; i >= 0; i--)
                {
                    var src = md[i].Source?.OriginalString ?? string.Empty;
                    if (src.EndsWith("Themes/LightTheme.xaml") || src.EndsWith("Themes/DarkTheme.xaml"))
                        md.RemoveAt(i);
                }

                // add requested overlay
                var overlay = name == "Dark" ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
                md.Add(new ResourceDictionary { Source = new Uri(overlay, UriKind.Relative) });
            }
            catch
            {
                // ignore errors at runtime theme apply
            }
        }

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
        /// 複数保持するプリセット（URL とそのキー/値エントリのセット）。
        /// </summary>
        public ObservableCollection<Preset> Presets { get; } = new ObservableCollection<Preset>();

        /// <summary>
        /// 現在選択されているプリセット。
        /// SelectedPreset を切り替えると、Entries がそのプリセットに合わせて切り替わります。
        /// </summary>
        private Preset? _selectedPreset;
        public Preset? SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (SetProperty(ref _selectedPreset, value))
                {
                    // 切り替え時に Entries を差し替える
                    Entries = _selectedPreset?.Entries ?? new ObservableCollection<Entry>();
                    // 選択エントリをリセット
                    SelectedEntry = null;
                    UpdateRenderedUrl();
                    // プリセット移動ボタンの状態を更新
                    MovePresetUpCommand?.RaiseCanExecuteChanged();
                    MovePresetDownCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// UI にバインドするエントリコレクション。内部的には SelectedPreset.Entries を参照します。
        /// </summary>
        private ObservableCollection<Entry> _entries = new ObservableCollection<Entry>();
        public ObservableCollection<Entry> Entries
        {
            get => _entries;
            private set
            {
                if (_entries == value)
                    return;
                if (_entries != null)
                    _entries.CollectionChanged -= Entries_CollectionChanged;
                _entries = value ?? new ObservableCollection<Entry>();
                _entries.CollectionChanged += Entries_CollectionChanged;
                RaisePropertyChanged(nameof(Entries));
            }
        }

        private Entry? _selectedEntry;
        public Entry? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (SetProperty(ref _selectedEntry, value))
                {
                    // 選択が変わったら移動ボタンの有効/無効を更新
                    MoveEntryUpCommand?.RaiseCanExecuteChanged();
                    MoveEntryDownCommand?.RaiseCanExecuteChanged();
                }
            }
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
        /// <summary>
        /// 現在選択されているプリセットの UrlTemplate を参照するプロパティ。
        /// SelectedPreset が null の場合は空文字列を返します。
        /// </summary>
        public string UrlTemplate
        {
            get => SelectedPreset?.UrlTemplate ?? string.Empty;
            set
            {
                if (SelectedPreset == null)
                    return;
                if (SelectedPreset.UrlTemplate != value)
                {
                    SelectedPreset.UrlTemplate = value;
                    // UrlTemplate が変わったことを通知
                    RaisePropertyChanged(nameof(UrlTemplate));
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

            // Preset 関連コマンド
            AddPresetCommand = new DelegateCommand(OnAddPreset);
            RemovePresetCommand = new DelegateCommand(OnRemovePreset, CanRemovePreset)
                .ObservesProperty(() => SelectedPreset);
            DuplicatePresetCommand = new DelegateCommand(OnDuplicatePreset, CanDuplicatePreset)
                .ObservesProperty(() => SelectedPreset);
            // Preset 移動コマンド初期化
            MovePresetUpCommand = new DelegateCommand(OnMovePresetUp, CanMovePresetUp)
                .ObservesProperty(() => SelectedPreset)
                .ObservesProperty(() => Presets.Count);
            MovePresetDownCommand = new DelegateCommand(OnMovePresetDown, CanMovePresetDown)
                .ObservesProperty(() => SelectedPreset)
                .ObservesProperty(() => Presets.Count);

            // エントリ移動コマンド初期化
            MoveEntryUpCommand = new DelegateCommand(OnMoveEntryUp, CanMoveEntryUp)
                .ObservesProperty(() => SelectedEntry)
                .ObservesProperty(() => Entries.Count);
            MoveEntryDownCommand = new DelegateCommand(OnMoveEntryDown, CanMoveEntryDown)
                .ObservesProperty(() => SelectedEntry)
                .ObservesProperty(() => Entries.Count);

            // 初期プリセットの作成（サンプル）
            var preset1 = new Preset { Name = "Default", Icon = "Link", Description = "サンプルプリセット", UrlTemplate = "http://xxxx.xxx.xxx:?param1={ExampleKey1}&param2={ExampleKey2}" };
            preset1.Entries.Add(new Entry { Key = "ExampleKey1", Value = "ExampleValue1" });
            preset1.Entries.Add(new Entry { Key = "ExampleKey2", Value = "ExampleValue2" });

            Presets.Add(preset1);
            SelectedPreset = preset1;
        }

        /// <summary>
        /// プリセット追加/削除用のコマンド
        /// </summary>
        public DelegateCommand AddPresetCommand { get; }
        public DelegateCommand RemovePresetCommand { get; }
        public DelegateCommand DuplicatePresetCommand { get; }
        public DelegateCommand MovePresetUpCommand { get; }
        public DelegateCommand MovePresetDownCommand { get; }
        public DelegateCommand MoveEntryUpCommand { get; }
        public DelegateCommand MoveEntryDownCommand { get; }

        private void OnAddPreset()
        {
            var p = new Preset { Name = $"Preset{Presets.Count + 1}", UrlTemplate = string.Empty };
            Presets.Add(p);
            SelectedPreset = p;
        }

        private void OnRemovePreset()
        {
            if (SelectedPreset != null)
            {
                Presets.Remove(SelectedPreset);
                SelectedPreset = Presets.FirstOrDefault();
            }
        }

        private bool CanRemovePreset()
            => SelectedPreset != null;

        private void OnDuplicatePreset()
        {
            if (SelectedPreset == null)
                return;

            // create deep copy of selected preset
            var original = SelectedPreset;
            // generate a unique name
            var baseName = original.Name + " - コピー";
            var newName = baseName;
            int i = 1;
            while (Presets.Any(p => p.Name == newName))
            {
                i++;
                newName = baseName + $" ({i})";
            }

            var copy = new Preset
            {
                Name = newName,
                UrlTemplate = original.UrlTemplate,
                Icon = original.Icon,
                Description = original.Description
            };

            foreach (var e in original.Entries)
            {
                copy.Entries.Add(new Entry { Key = e.Key, Value = e.Value });
            }

            Presets.Add(copy);
            SelectedPreset = copy;
        }

        private bool CanDuplicatePreset()
            => SelectedPreset != null;

        private void OnMovePresetUp()
        {
            if (SelectedPreset == null)
                return;
            var idx = Presets.IndexOf(SelectedPreset);
            if (idx > 0)
            {
                try
                {
                    Presets.Move(idx, idx - 1);
                    // Force selection refresh: temporarily clear and re-select moved item
                    var moved = Presets[idx - 1];
                    SelectedPreset = null;
                    SelectedPreset = moved;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"プリセットの移動に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                MovePresetUpCommand?.RaiseCanExecuteChanged();
                MovePresetDownCommand?.RaiseCanExecuteChanged();
            }
        }

        private void OnMovePresetDown()
        {
            if (SelectedPreset == null)
                return;
            var idx = Presets.IndexOf(SelectedPreset);
            if (idx >= 0 && idx < Presets.Count - 1)
            {
                try
                {
                    Presets.Move(idx, idx + 1);
                    var moved = Presets[idx + 1];
                    SelectedPreset = null;
                    SelectedPreset = moved;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"プリセットの移動に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                MovePresetUpCommand?.RaiseCanExecuteChanged();
                MovePresetDownCommand?.RaiseCanExecuteChanged();
            }
        }

        private bool CanMovePresetUp() => SelectedPreset != null && Presets.IndexOf(SelectedPreset) > 0;
        private bool CanMovePresetDown() => SelectedPreset != null && Presets.IndexOf(SelectedPreset) >= 0 && Presets.IndexOf(SelectedPreset) < Presets.Count - 1;

        /// <summary>アプリケーションを終了します。</summary>
        private void OnExit()
        {
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>空のエントリを追加して選択します（デフォルトのキー/値を設定）。</summary>
        private void OnAddEntry()
        {
            // デフォルトのキー/値を設定して追加（param1, param2... と value1, value2...）
            var baseKey = "param";
            int idx = 1;
            string keyName = baseKey + idx;
            while (Entries.Any(e => e.Key == keyName))
            {
                idx++;
                keyName = baseKey + idx;
            }
            var entry = new Entry { Key = keyName, Value = "value" + idx };
            Entries.Add(entry);
            SelectedEntry = entry;
        }

        private void OnMoveEntryUp()
        {
            try
            {
                if (SelectedEntry == null)
                    return;
                var idx = Entries.IndexOf(SelectedEntry);
                if (idx > 0)
                {
                    Entries.Move(idx, idx - 1);
                    SelectedEntry = Entries[idx - 1];
                    MoveEntryUpCommand?.RaiseCanExecuteChanged();
                    MoveEntryDownCommand?.RaiseCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エントリの移動に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnMoveEntryDown()
        {
            try
            {
                if (SelectedEntry == null)
                    return;
                var idx = Entries.IndexOf(SelectedEntry);
                if (idx >= 0 && idx < Entries.Count - 1)
                {
                    Entries.Move(idx, idx + 1);
                    SelectedEntry = Entries[idx + 1];
                    MoveEntryUpCommand?.RaiseCanExecuteChanged();
                    MoveEntryDownCommand?.RaiseCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エントリの移動に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanMoveEntryUp() => SelectedEntry != null && Entries.IndexOf(SelectedEntry) > 0;
        private bool CanMoveEntryDown() => SelectedEntry != null && Entries.IndexOf(SelectedEntry) >= 0 && Entries.IndexOf(SelectedEntry) < Entries.Count - 1;

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
            MoveEntryUpCommand?.RaiseCanExecuteChanged();
            MoveEntryDownCommand?.RaiseCanExecuteChanged();
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
                {
                    // エントリの値に '%' が含まれている場合は URL エンコードして置換する
                    if (!string.IsNullOrEmpty(val) && val.Contains('%'))
                        return Uri.EscapeDataString(val);
                    return val ?? string.Empty;
                }
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
                Presets = this.Presets.Select(p => new PresetSettings
                {
                    Name = p.Name,
                    UrlTemplate = p.UrlTemplate ?? string.Empty,
                    Entries = p.Entries.Select(e => new SettingsEntry { Key = e.Key ?? string.Empty, Value = e.Value ?? string.Empty }).ToList()
                }).ToList()
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
                    Presets.Clear();
                    if (data.Presets != null)
                    {
                        foreach (var ps in data.Presets)
                        {
                            var p = new Preset { Name = ps.Name, UrlTemplate = ps.UrlTemplate, Icon = ps.Icon ?? "Link", Description = ps.Description ?? string.Empty };
                            if (ps.Entries != null)
                            {
                                foreach (var se in ps.Entries)
                                {
                                    p.Entries.Add(new Entry { Key = se.Key, Value = se.Value });
                                }
                            }
                            Presets.Add(p);
                        }
                    }

                    SelectedPreset = Presets.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"読み込みに失敗しました: {ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 設定の保存/読み込み時に XML シリアル化で使用するデータ構造（複数プリセット対応）。
        /// </summary>
        [Serializable]
        public class SettingsData
        {
            public List<PresetSettings> Presets { get; set; } = new List<PresetSettings>();
        }

        [Serializable]
        public class PresetSettings
        {
            public string Name { get; set; } = string.Empty;
            public string UrlTemplate { get; set; } = string.Empty;
            public string Icon { get; set; } = "Link";
            public string Description { get; set; } = string.Empty;
            public List<SettingsEntry> Entries { get; set; } = new List<SettingsEntry>();
        }

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