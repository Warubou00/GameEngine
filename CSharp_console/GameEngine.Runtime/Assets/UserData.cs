using System.Text.Json;

namespace GameEngine;

// ==========================================================================
// UserData (v2.0 UD1。Unity の ScriptableObject / RE Engine の UserData に当たる)
//   エディターを使う人が自分で決められる「設定のファイル」(名前.userdata.json)。中身はクラスの public なフィールド
//
//   [UserData(Menu = "Game/Enemy Params", FileName = "New Enemy Params")]
//   public sealed class EnemyParams : UserData
//   {
//       public int Hp = 100;
//       [Range(0, 20)] public float Speed = 3;
//       public List<int> DropIds = new();
//       public List<Wave> Waves = new();   // 入れ子の struct / [Serializable] なクラスのリストも
//       public Prefab? Bullet;             // アセット (プレハブ / モデル / 音 / 別の UserData ...)
//   }
//   var p = engine.Assets.LoadUserData<EnemyParams>("Assets/Data/Goblin.userdata.json");
//
//   - 同じパスは同じインスタンス (使う物どうしで共有)。ファイルが変わると同じインスタンスの中身を差し替え、Version が上がって Changed が呼ばれる
//   - コンポーネントのフィールドにも書ける (public EnemyParams? Params;)。保存はアセットの {guid, path}
//   - シーンの物 (Entity / GameObject / ComponentRef<T>) は持てない (Unity / RE Engine と同じ。アセットはシーンの外にあるので)。
//     そのフィールドは保存されず、読み込むと空。物を作る元が欲しい時はプレハブを使う
//   - 保存するフィールドはコンポーネントと同じ決まり (public か [SerializeField]。readonly / [NonSerialized] は保存しない)
// ==========================================================================
public abstract class UserData
{
    /// <summary>アセットのパス ("Assets/...")。コードで new した物は null (保存するまで)</summary>
    public string? Path { get; internal set; }
    /// <summary>アセットの Id (.meta の guid。無ければ Empty)</summary>
    //   v2.0 UD2 : まだ .meta が無かった時 (作った直後) は、後で引けるようになったら覚える (以前は保存した時の値のままで、作った直後の物は Empty のままだった)
    public Guid AssetId
    {
        get
        {
            if (_assetId == Guid.Empty && Path != null && AssetDatabase.Current?.Find(Path) is { } entry) _assetId = entry.Guid;
            return _assetId;
        }
        internal set => _assetId = value;
    }
    private Guid _assetId;
    /// <summary>中身が変わった回数 (ファイルを読み直した / エディターで変えた時に上がる)</summary>
    public int Version { get; private set; }
    /// <summary>中身が変わった時 (ファイルを読み直した / エディターで変えた時)</summary>
    public event Action<UserData>? Changed;

    /// <summary>このファイルの名前 (拡張子無し。パスが無ければ型の名前)</summary>
    public string Name => Path == null ? GetType().Name : UserDataFile.NameOf(Path);

    // ファイルにあって型に無い値 (フィールドの名前を変えた / 消した時)。保存する時にそのまま書き戻す (値を失わないように)
    [NonSerialized] internal Dictionary<string, JsonElement>? UnknownValues;
    /// <summary>ファイルにあって型に無い値の名前 (保存する時に書き戻す。エディターが出す。v2.0 UD2)</summary>
    public IReadOnlyCollection<string> UnknownValueNames => UnknownValues?.Keys ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>読み込んだ後 (ファイルを読み直した後も) に呼ばれる</summary>
    protected virtual void OnLoaded() { }
    /// <summary>エディターで値を変えた後に呼ばれる (値の範囲を直すなど)</summary>
    protected virtual void OnValidate() { }

    internal void NotifyLoaded() => OnLoaded();
    /// <summary>OnValidate を呼ぶ (エディターが値を変えた後に呼ぶ。v2.0 UD2)</summary>
    public void Validate() => OnValidate();

    /// <summary>中身が変わったことを知らせる (Version を上げて Changed を呼ぶ)</summary>
    public void MarkChanged()
    {
        Version++;
        Changed?.Invoke(this);
    }

    /// <summary>中身を JSON にする (ファイルと同じ形)</summary>
    public string ToJson() => SceneSerializer.SaveUserData(this);

    public override string ToString() => $"{GetType().Name}({Path ?? "(not saved)"})";
}

/// <summary>
/// UserData の型の情報 (v2.0)。Menu = エディターの Create > UserData の下の名前 ("Game/Enemy Params" のように / で区切る)。
/// FileName = 作る時のファイルの名前 (拡張子無し)
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UserDataAttribute : Attribute
{
    public string? Menu { get; set; }
    public string? FileName { get; set; }
}

/// <summary>UserData のファイルの決まり (v2.0)</summary>
public static class UserDataFile
{
    public const string Extension = ".userdata.json";
    public const string Format    = "GameEngineUserData";
    public const int    Version   = 1;

    /// <summary>パスのファイルの名前から拡張子を除いた物</summary>
    public static string NameOf(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? name[..^Extension.Length] : System.IO.Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>JSON の型の名前 ("type") を読む (中身は読まない。読めなければ null)</summary>
    public static string? ReadTypeName(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// UserData の型を探す (v2.0)。ComponentRegistry と同じく、エンジンとエンジンを参照するアセンブリ (ゲームのコード) の
/// public で abstract でない UserData の派生クラス。読み直して捨てたゲームの DLL は見ない (ComponentRegistry.Forget)
/// </summary>
public static class UserDataTypes
{
    /// <summary>保存した名前 (FullName) から型を探す (無ければ null。後から読み込んだアセンブリも探し直す)</summary>
    public static Type? Find(string typeName) => ComponentRegistry.FindUserDataType(typeName);

    /// <summary>UserData の型の一覧 (名前の順。エディターの Create メニュー用)</summary>
    public static IReadOnlyList<Type> Known(bool rescan = false) => ComponentRegistry.KnownUserDataTypes(rescan);

    /// <summary>[UserData] の Menu (無ければ型の名前)</summary>
    public static string MenuOf(Type type) =>
        (type.GetCustomAttributes(typeof(UserDataAttribute), false).FirstOrDefault() as UserDataAttribute)?.Menu is { Length: > 0 } menu ? menu : type.Name;

    /// <summary>[UserData] の FileName (無ければ "New 型の名前")</summary>
    public static string FileNameOf(Type type) =>
        (type.GetCustomAttributes(typeof(UserDataAttribute), false).FirstOrDefault() as UserDataAttribute)?.FileName is { Length: > 0 } name ? name : $"New {type.Name}";

    /// <summary>
    /// UserData の中に値として書ける [Serializable] なクラスか (abstract でない / 引数なしのコンストラクターがある / .NET の型・リスト・UserData でない)
    /// </summary>
    public static bool IsSerializableClass(Type type) =>
        type.IsClass && !type.IsAbstract && type != typeof(string) && type.IsDefined(typeof(SerializableAttribute), false) &&   // [Serializable] (Type.IsSerializable は旧形式の警告が出るので使わない)
        type.Assembly != typeof(object).Assembly &&                 // .NET の型 (Dictionary など) は入れない
        !typeof(UserData).IsAssignableFrom(type) && !typeof(System.Collections.IList).IsAssignableFrom(type) &&
        type.GetConstructor(Type.EmptyTypes) != null;

    /// <summary>シーンの物への参照の型か (UserData には保存しない)</summary>
    public static bool IsSceneReference(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(Entity) || type == typeof(GameObject) || EntityRefs.IsEntityRef(type);
    }
}
