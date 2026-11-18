using System.Numerics;
using System.Runtime.CompilerServices;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// インスペクターの UserData (v2.0 UD2)
//   - Project で .userdata.json を選ぶと、型の保存するフィールドを並べる (コンポーネントと同じ部品 : Range / Header / Tooltip / リスト /
//     入れ子の struct / [Serializable] なクラス / アセットの欄 / 別の UserData の欄)
//   - 変えたら OnValidate を呼び、触り終わったらファイルに書く (マテリアルと同じ)。元に戻せる (UserDataEdit。シーンの未保存の印は付けない)
//   - シーンの物への参照 (GameObject / Entity / ComponentRef) の欄は出すが触れない (アセットはシーンの外にあるので。理由を出す)
//   - 再生中に変えた値はファイルに書かない (止めるとファイルの値に戻る)
//   - 型が見つからない時 (ゲームのコードのビルドの失敗など) はファイルの中身を見るだけ (書き換えない)
//   - インスタンスや型は持たない (パスと JSON だけ。ゲームの DLL を読み直すと型が変わるので)
// ==========================================================================
internal sealed partial class Inspector
{
    private static readonly AssetKind[] UserDataKinds = { AssetKind.UserData };

    private string? _udPath;          // 今出している UserData のパス
    private string? _udSnapshot;      // 最後に保存した (読んだ) 時の JSON (元に戻す時の「前」)
    private int     _udSeenVersion;   // 自分が見た Version (外で読み直された時に写しを作り直す)
    private int     _udInstance;      // 見ていたインスタンスの印 (ゲームの DLL を読み直すと別の物になる。参照は持たない)
    private bool    _udDirty;         // 変えたがまだ書いていない
    private bool    _drawingUserData; // UserData の欄を描いている間 ([Serializable] なクラスの欄を出す)
    private readonly Dictionary<string, (DateTime Time, string? TypeName)> _udTypeNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>UserData の欄のダブルクリック : Project でそのアセットを選ぶ</summary>
    public Action<string>? SelectAsset;

    /// <summary>今出している UserData のパス (確認用)</summary>
    internal string? EditingUserDataPath => _udPath;
    internal bool UserDataDirty => _udDirty;

    /// <summary>UserData を出す。playing : 再生中 (変えた値をファイルに書かない)</summary>
    public void DrawUserData(string path, bool playing)
    {
        UserData data;
        try
        {
            data = _engine.Assets.LoadUserData(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            FlushUserData();
            _udPath = null;
            drawUnreadableUserData(path, ex.Message);
            return;
        }

        var instance = RuntimeHelpers.GetHashCode(data);
        if (_udPath != path)
        {
            FlushUserData();
            beginUserData(path, data);
        }
        else if (instance != _udInstance || (data.Version != _udSeenVersion && !_udDirty))
        {
            beginUserData(path, data);   // 外で読み直された / 元に戻した / ゲームの DLL を読み直した
        }

        var type = data.GetType();
        ImGui.Text(data.Name);
        ImGui.TextDisabled(path);
        ImGui.TextDisabled($"UserData : {type.FullName}");
        if (playing) ImGui.TextColored(new Vector4(1.0f, 0.75f, 0.3f, 1.0f), "Playing : changes are not saved (the file values come back on Stop).");
        if (data.UnknownValueNames is { Count: > 0 } unknown)
        {
            ImGui.TextDisabled($"Kept from the file (not in {type.Name}) : {string.Join(", ", unknown)}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("These values are written back when saving, so renaming a field does not lose them.");
        }
        ImGui.Separator();

        var changed = false;
        _drawingUserData = true;
        try
        {
            foreach (var field in ComponentType.SerializedFields(type))
            {
                if (field.HideInInspector) continue;
                if (field.Header != null) ImGui.SeparatorText(field.Header);
                if (UserDataTypes.IsSceneReference(field.FieldType))
                {
                    ImGui.BeginDisabled();
                    ImGui.LabelText(field.Name, "(scene objects cannot be referenced)");
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip("UserData is an asset outside scenes, so it cannot hold scene objects.\nUse a Prefab field to reference objects to create.");
                    continue;
                }
                var value = field.GetValue(data);
                if (drawValue(field.Name, field.FieldType, ref value, field.Range, Entity.Null, field))
                {
                    field.SetValue(data, value);
                    changed = true;
                }
                if (field.Tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(field.Tooltip);
            }
        }
        finally
        {
            _drawingUserData = false;
        }

        if (changed)
        {
            try { data.Validate(); }
            catch (Exception ex) { Console.WriteLine($"[UserData] {type.Name}.OnValidate : {ex.Message}"); }
            data.MarkChanged();
            _udSeenVersion = data.Version;
            _udDirty = true;
        }
        if (_udDirty && !playing && !ImGui.IsAnyItemActive()) FlushUserData();
        ImGui.Spacing();
        ImGui.TextDisabled("Use it from a component field (public " + type.Name + "? Field;) or engine.Assets.LoadUserData.");
    }

    private void beginUserData(string path, UserData data)
    {
        _udPath = path;
        _udSnapshot = data.ToJson();
        _udSeenVersion = data.Version;
        _udInstance = RuntimeHelpers.GetHashCode(data);
        _udDirty = false;
    }

    /// <summary>変えた UserData をファイルに書き、元に戻す記録を積む (触り終わった時 / 別の物を選んだ時)</summary>
    public void FlushUserData()
    {
        if (!_udDirty || _udPath == null) return;
        _udDirty = false;
        if (_engine.Assets.FindLoadedUserData(_udPath) is not { } data) return;
        try
        {
            var after = data.ToJson();
            if (after == _udSnapshot) return;
            _engine.Assets.SaveUserData(data);
            _undo.Record(new UserDataEdit(_udPath, _udSnapshot ?? after, after));
            _udSnapshot = after;
            _udSeenVersion = data.Version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.WriteLine($"[UserData] Save failed {_udPath} : {ex.Message}");
        }
    }

    /// <summary>
    /// 確認用 : 今出している UserData を Inspector で変えたのと同じに扱う (OnValidate → Version → 触り終わったら書く)。
    /// ImGui の欄を操作しないで、保存 / 元に戻す / 再生中の扱いを確かめるため
    /// </summary>
    internal bool EditUserDataForTest(Action<UserData> edit)
    {
        if (_udPath == null || _engine.Assets.FindLoadedUserData(_udPath) is not { } data) return false;
        edit(data);
        data.Validate();
        data.MarkChanged();
        _udSeenVersion = data.Version;
        _udDirty = true;
        return true;
    }

    /// <summary>今の編集を捨てる (再生を止めた時。値はファイルの物に戻っている)</summary>
    public void ResetUserDataEditing()
    {
        _udPath = null;
        _udDirty = false;
        _udSnapshot = null;
    }

    // 読めない UserData (型が見つからない / 壊れたファイル) : 理由と中身を見せるだけ
    private void drawUnreadableUserData(string path, string reason)
    {
        ImGui.Text(UserDataFile.NameOf(path));
        ImGui.TextDisabled(path);
        ImGui.PushTextWrapPos();
        ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), reason);
        ImGui.TextDisabled("The file is shown as is and is not changed. Fix the script (or the file) to edit it.");
        ImGui.PopTextWrapPos();
        ImGui.Separator();
        string text;
        try
        {
            var file = AssetDatabase.Current?.FullPath(path) ?? path;
            text = File.ReadAllText(file);
            if (text.Length > 32 * 1024) text = text[..(32 * 1024)] + "\n...";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text = ex.Message;
        }
        ImGui.InputTextMultiline("##raw", ref text, (uint)text.Length + 1, new Vector2(-1, Math.Max(120, ImGui.GetContentRegionAvail().Y - 8)), ImGuiInputTextFlags.ReadOnly);
    }

    // ---------------------------------------------------------------------
    // UserData の欄 (コンポーネント / UserData のフィールド)
    // ---------------------------------------------------------------------

    private bool drawUserDataField(string label, Type type, ref object? value)
    {
        var path = (value as UserData)?.Path ?? "";
        var assets = _engine.Assets;
        return drawAssetField(label, UserDataKinds, path, ref value, p =>
        {
            var data = assets.LoadUserData(p);
            return type.IsInstanceOfType(data) ? data : throw new InvalidDataException($"{UserDataFile.NameOf(p)} is {data.GetType().Name}, not {type.Name}.");
        }, type, p => SelectAsset?.Invoke(p));
    }

    /// <summary>そのアセットが type (か派生の型) の UserData か (ファイルの "type" を読む。更新時刻が変わるまで覚える)</summary>
    internal bool IsUserDataOf(AssetEntry entry, Type type)
    {
        if (entry.Kind != AssetKind.UserData) return false;
        var file = AssetDatabase.Current?.FullPath(entry.Path) ?? entry.Path;
        DateTime time;
        try { time = File.GetLastWriteTimeUtc(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        if (!_udTypeNames.TryGetValue(entry.Path, out var cached) || cached.Time != time)
        {
            string? name = null;
            try { name = UserDataFile.ReadTypeName(File.ReadAllText(file)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _udTypeNames[entry.Path] = cached = (time, name);
        }
        if (cached.TypeName == null) return false;
        if (cached.TypeName == type.FullName) return true;
        return UserDataTypes.Find(cached.TypeName) is { } fileType && type.IsAssignableFrom(fileType);
    }

    // [Serializable] なクラスの欄 (null なら作るボタン)。中身はその場で書き換える
    private bool drawSerializableClass(string label, Type type, ref object? value, Entity owner)
    {
        if (value == null)
        {
            ImGui.LabelText(label, "(null)");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Create##{label}"))
            {
                value = Activator.CreateInstance(type);
                return true;
            }
            return false;
        }
        var changed = false;
        if (ImGui.TreeNode(label))
        {
            foreach (var inner in ComponentType.SerializedFields(type))
            {
                if (inner.HideInInspector) continue;
                if (inner.Header != null) ImGui.SeparatorText(inner.Header);
                if (UserDataTypes.IsSceneReference(inner.FieldType))
                {
                    ImGui.BeginDisabled();
                    ImGui.LabelText(inner.Name, "(scene objects cannot be referenced)");
                    ImGui.EndDisabled();
                    continue;
                }
                var innerValue = inner.GetValue(value);
                if (drawValue(inner.Name, inner.FieldType, ref innerValue, inner.Range, owner, inner))
                {
                    inner.SetValue(value, innerValue);
                    changed = true;
                }
                if (inner.Tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(inner.Tooltip);
            }
            if (ImGui.SmallButton("Clear (null)")) { value = null; changed = true; }
            ImGui.TreePop();
        }
        return changed;
    }
}
