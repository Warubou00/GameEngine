using System.Text;
using System.Text.Json;

namespace GameEngine;

// ==========================================================================
// UserData (.userdata.json) の読み書き (v2.0 UD1)
//   {
//     "format": "GameEngineUserData", "version": 1,
//     "type": "Game.EnemyParams",
//     "values": { "Hp": 100, "Speed": 3, "Bullet": { "guid": "...", "path": "Assets/..." }, ... }
//   }
//   - 値の書き方はシーンと同じ (writeValue / readValue をそのまま使う)。違いは 2 つ (UserDataMode) :
//       シーンの物への参照 (Entity / GameObject / ComponentRef<T>) は書かない / 読まない
//       [Serializable] なクラス (と、そのリスト) も書ける
//   - 型に無い値 ("values" の知らない名前) は UnknownValues に取っておき、保存する時に書き戻す (名前を変えた時に値を失わない)
// ==========================================================================
public static partial class SceneSerializer
{
    // 入れ子のクラスの深さの上限 (自分を指すクラスで止まらないように。Unity と同じ考え)
    private const int MaxClassDepth = 10;

    // UserData はシーンの外の物なので、読み書きの World は空の物 (シーンの物への参照は上の UserDataMode で先に止める)
    private static readonly World _noScene = new();

    // UD2 : エディター (リストに足す時) も使うので UserDataTypes に移した
    private static bool isSerializableClass(Type type) => UserDataTypes.IsSerializableClass(type);

    /// <summary>UserData を JSON にする (ファイルと同じ形)</summary>
    public static string SaveUserData(UserData data)
    {
        var context = new WriteContext(_noScene) { UserDataMode = true };
        var type = data.GetType();
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", UserDataFile.Format);
            w.WriteNumber("version", UserDataFile.Version);
            w.WriteString("type", type.FullName);
            w.WriteStartObject("values");
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in ComponentType.SerializedFields(type))
            {
                w.WritePropertyName(field.Name);
                writeValue(w, field.FieldType, field.GetValue(data), context);
                written.Add(field.Name);
            }
            // 型に無い値 (前のファイルにあった物) をそのまま書き戻す
            if (data.UnknownValues != null)
            {
                foreach (var (name, value) in data.UnknownValues)
                {
                    if (!written.Add(name)) continue;
                    w.WritePropertyName(name);
                    value.WriteTo(w);
                }
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// JSON の "type" の UserData を作る (中身はまだ入れない)。型が無い / UserData でない / 引数なしのコンストラクターが無い時は例外
    /// </summary>
    internal static UserData CreateUserData(string json, string pathForMessage)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || (root.TryGetProperty("format", out var format) && format.GetString() != UserDataFile.Format))
            throw new InvalidDataException($"Not a UserData file : {pathForMessage}");
        var typeName = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(typeName)) throw new InvalidDataException($"UserData has no type : {pathForMessage}");
        var type = UserDataTypes.Find(typeName) ?? throw new InvalidDataException($"Unknown UserData type '{typeName}' : {pathForMessage}");
        return createInstance(type);
    }

    private static UserData createInstance(Type type)
    {
        if (type.GetConstructor(Type.EmptyTypes) == null) throw new InvalidDataException($"UserData type {type.FullName} needs a public parameterless constructor.");
        return (UserData)Activator.CreateInstance(type)!;
    }

    /// <summary>
    /// JSON の値を data に入れる。resetFirst : 先に全部のフィールドを型の既定の値に戻す (ファイルを読み直した時。ファイルから消した値が前のまま残らないように)
    /// 読めない値は飛ばして warnings に入れる。JSON の型が data の型と違う時は例外 (何も変えない)
    /// </summary>
    internal static void PopulateUserData(UserData data, string json, AssetManager? assets, List<string> warnings, bool resetFirst)
    {
        var type = data.GetType();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        var typeName = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (typeName != type.FullName) throw new InvalidDataException($"UserData type changed ({type.FullName} -> {typeName ?? "(none)"}) : {data.Path}");

        var fields = ComponentType.SerializedFields(type);
        if (resetFirst)
        {
            var fresh = createInstance(type);
            foreach (var field in fields) field.SetValue(data, field.GetValue(fresh));
        }
        data.UnknownValues = null;
        if (!root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object) return;

        var context = new ReadContext(_noScene, assets) { UserDataMode = true };
        foreach (var field in fields)
        {
            if (!values.TryGetProperty(field.Name, out var value)) continue;   // 無い値は既定のまま
            try
            {
                field.SetValue(data, readValue(value, field.FieldType, context));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                warnings.Add($"{type.Name}.{field.Name} : {ex.Message}");
            }
        }
        warnings.AddRange(context.Warnings);
        foreach (var property in values.EnumerateObject())
        {
            if (fields.Any(f => f.Name == property.Name)) continue;
            (data.UnknownValues ??= new(StringComparer.Ordinal))[property.Name] = property.Value.Clone();
        }
    }
}
