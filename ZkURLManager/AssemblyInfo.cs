using System.Windows;

// ThemeInfo 属性は WPF に対してテーマ固有および汎用の
// リソース辞書の検索先を知らせます。これによりコントロールが
// リソースを要求したときにスタイルやブラシなどを正しく見つけることができます。
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            // このアセンブリではテーマ固有のリソース辞書を使用しません
                                                // （ページやアプリケーションのリソース辞書に見つからない場合に使用されます）
    ResourceDictionaryLocation.SourceAssembly   // 汎用のリソース辞書はこのアセンブリにあります
                                                // （それ以外でリソースが見つからない場合のフォールバックです）
)]
