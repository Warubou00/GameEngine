namespace GameEngine;

// ==========================================================================
// Widget のアセット (.widget.json) の読み込み (v1.7)
// ==========================================================================
public sealed partial class AssetManager
{
    private readonly Dictionary<string, WidgetAsset> _widgets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Widget のアセットを読む。同じパスは同じ WidgetAsset を返す</summary>
    public WidgetAsset LoadWidget(string path)
    {
        (path, var file) = resolve(path);
        if (_widgets.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load widget: {path}", path);
        var widget = new WidgetAsset(path, File.ReadAllText(file, System.Text.Encoding.UTF8));
        _widgets.Add(path, widget);
        return widget;
    }

    /// <summary>ファイルが変わった時 : 読み直す (これから作る物が新しい中身になる。もう作った物はそのまま)</summary>
    public bool ReloadWidget(string path)
    {
        (path, var file) = resolve(path);
        if (!_widgets.TryGetValue(path, out var widget) || !File.Exists(file)) return false;
        widget.SetJson(File.ReadAllText(file, System.Text.Encoding.UTF8));
        AssetReloaded?.Invoke(path);
        return true;
    }

    public IEnumerable<WidgetAsset> LoadedWidgets => _widgets.Values;
}
