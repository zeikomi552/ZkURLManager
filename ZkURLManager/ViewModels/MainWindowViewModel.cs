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
    public class MainWindowViewModel : BindableBase
    {
        private string _title = "ZkURLManager";
        public string Title 
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public DelegateCommand ExitCommand { get; }
        public ObservableCollection<Entry> Entries { get; } = new ObservableCollection<Entry>();
        private Entry _selectedEntry;
        public Entry SelectedEntry
        {
            get => _selectedEntry;
            set => SetProperty(ref _selectedEntry, value);
        }

        public DelegateCommand AddEntryCommand { get; }
        public DelegateCommand RemoveEntryCommand { get; }
        public DelegateCommand OpenUrlCommand { get; }
        public DelegateCommand SaveCommand { get; }
        public DelegateCommand LoadCommand { get; }
        public DelegateCommand CopyParametersCommand { get; }
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
            // build template like param1={param1}&param2={param2} from entries keys
            var sb = new StringBuilder();
            bool first = true;
            foreach (var e in Entries)
            {
                if (string.IsNullOrEmpty(e.Key))
                    continue;
                if (!first)
                    sb.Append('&');
                first = false;
                // key should be left as-is for template, but braces should surround the key
                var encodedKey = Uri.EscapeDataString(e.Key);
                // Use placeholder with original key (not URL-escaped) inside braces
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

        private bool CanCopyParameters()
        {
            return Entries.Count > 0;
        }

        private void OnCopyParameters()
        {
            // build param string like param1=value1&param2=value2 from entries
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

        private string _renderedUrl = string.Empty;
        public string RenderedUrl
        {
            get => _renderedUrl;
            private set => SetProperty(ref _renderedUrl, value);
        }

        public MainWindowViewModel()
        {
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

            // watch collection changes so we can recompute preview when entries change
            Entries.CollectionChanged += Entries_CollectionChanged;

            // sample data
            Entries.Add(new Entry { Key = "ExampleKey1", Value = "ExampleValue1" });
            Entries.Add(new Entry { Key = "ExampleKey2", Value = "ExampleValue2" });

            // default template example
            UrlTemplate = "http://xxxx.xxx.xxx:?param1={ExampleKey1}&param2={ExampleKey2}";
        }

        private void OnExit()
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void OnAddEntry()
        {
            var entry = new Entry { Key = "", Value = "" };
            Entries.Add(entry);
            SelectedEntry = entry;
        }

        private void OnRemoveEntry()
        {
            if (SelectedEntry != null)
            {
                Entries.Remove(SelectedEntry);
                SelectedEntry = null;
            }
        }

        private bool CanRemoveEntry()
        {
            return SelectedEntry != null;
        }

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

            UpdateRenderedUrl();
        }

        private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // any change to entries should update preview
            UpdateRenderedUrl();
        }

        private void UpdateRenderedUrl()
        {
            if (string.IsNullOrEmpty(UrlTemplate))
            {
                RenderedUrl = string.Empty;
                return;
            }

            // build lookup from entries; later entries override earlier ones if duplicate keys
            var lookup = Entries.Where(x => !string.IsNullOrEmpty(x.Key))
                                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

            // replace placeholders like {key} with corresponding values
            string result = Regex.Replace(UrlTemplate, "\\{([^}]+)\\}", match =>
            {
                var key = match.Groups[1].Value;
                if (lookup.TryGetValue(key, out var val))
                    return val ?? string.Empty;
                return string.Empty; // not found -> empty
            });

            RenderedUrl = result;
        }

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

        private bool CanOpenUrl()
        {
            return !string.IsNullOrWhiteSpace(RenderedUrl);
        }

        private bool CanSave()
        {
            // allow save when there is a template or any entries
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

        [Serializable]
        public class SettingsData
        {
            public string UrlTemplate { get; set; } = string.Empty;
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