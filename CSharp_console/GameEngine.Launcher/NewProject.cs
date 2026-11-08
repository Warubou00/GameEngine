using System.Reflection;
using System.Text;

namespace GameEngine.Launcher;

// ==========================================================================
// 新しいプロジェクトを作る (v1.3。ランチャーの Projects > New Project...)
//   <場所>/<名前>/
//     Assets/Scenes/              シーンの置き場 (最初のシーン Main.scene.json はエディターが初めて開いた時に作る)
//     Scripts/Game.csproj         ゲームのコード (Assets の .cs もビルドする)
//     Scripts/Editor/GameEditor.csproj  エディターの拡張
//     .gitignore                  ビルドした物 / キャッシュを git に入れない
//   csproj はエンジンの見本のプロジェクト (CSharp_console/Project/Scripts) の物をランチャーに埋め込んで写す
//   (リポジトリの外ではエディターが渡す EngineAssemblyDir の DLL を参照するので、そのまま使える)
// ==========================================================================
internal static class NewProject
{
    public const string DefaultName = "New Project";

    /// <summary>名前に使えない字 / 空 / もうある時は理由を返す (よければ null)</summary>
    public static string? Validate(string parent, string name)
    {
        name = name.Trim();
        if (name.Length == 0) return "Enter a project name";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.')) return $"The name has characters that cannot be used in a folder name : {name}";
        if (string.IsNullOrWhiteSpace(parent)) return "Choose a location";
        var folder = Path.Combine(parent, name);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) return $"The folder already exists and is not empty : {folder}";
        return null;
    }

    /// <summary>プロジェクトを作る。戻り値 = プロジェクトのフォルダ</summary>
    public static string Create(string parent, string name)
    {
        name = name.Trim();
        if (Validate(parent, name) is { } error) throw new InvalidOperationException(error);
        var root = Path.GetFullPath(Path.Combine(parent, name));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scenes"));
        Directory.CreateDirectory(Path.Combine(root, "Scripts", "Editor"));
        File.WriteAllText(Path.Combine(root, "Scripts", "Game.csproj"), template("Game.csproj"), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, "Scripts", "Editor", "GameEditor.csproj"), template("GameEditor.csproj"), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(root, ".gitignore"),
            "# Tsumugi Engine のプロジェクト : ビルドした物 / キャッシュ (いつでも作り直せる)\r\nbin/\r\nobj/\r\nLibrary/\r\nBuild/\r\n*.sln\r\nimgui.ini\r\n",
            new UTF8Encoding(false));
        return root;
    }

    // 埋め込んだ csproj (GameEngine.Launcher.csproj の EmbeddedResource)
    private static string template(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"ProjectTemplate/{name}")
                           ?? throw new InvalidOperationException($"Template not found : {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>新しいプロジェクトの既定の場所 (Package : Package/Project / それ以外 : ドキュメント/Tsumugi Engine Projects)</summary>
    public static string DefaultLocation() =>
        EditorInstalls.PackageRoot is { } package ? Path.Combine(package, "Project")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tsumugi Engine Projects");

    /// <summary>まだ無い名前 ("New Project", "New Project 1", ...)</summary>
    public static string UniqueName(string parent)
    {
        var name = DefaultName;
        for (int i = 1; Directory.Exists(Path.Combine(parent, name)); i++) name = $"{DefaultName} {i}";
        return name;
    }
}

// 名前と場所を聞く窓
internal sealed class NewProjectDialog : Form
{
    private readonly TextBox _name = new() { Width = 360 };
    private readonly TextBox _location = new() { Width = 360 };
    private readonly CheckBox _open = new() { Text = "Open the project after creating it", Checked = true, AutoSize = true };
    private readonly Label _error = new() { AutoSize = true, ForeColor = Color.Firebrick };

    public string ProjectName => _name.Text.Trim();
    public string Location => _location.Text.Trim();
    public bool OpenAfter => _open.Checked;

    public NewProjectDialog()
    {
        Text = "New Project";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Font = new Font("Yu Gothic UI", 9.5f);
        _location.Text = NewProject.DefaultLocation();
        _name.Text = NewProject.UniqueName(_location.Text);

        var layout = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = "Name", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(new Label { Text = "Location", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_location, 1, 1);
        var browse = new Button { Text = "...", Width = 32 };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _location.Text, ShowNewFolderButton = true, Description = "Where to create the project", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) _location.Text = dialog.SelectedPath;
        };
        layout.Controls.Add(browse, 2, 1);
        layout.Controls.Add(_open, 1, 2);
        layout.Controls.Add(_error, 1, 3);
        var create = new Button { Text = "Create", Width = 100 };
        var cancel = new Button { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
        create.Click += (_, _) =>
        {
            if (NewProject.Validate(Location, ProjectName) is { } error) { _error.Text = error; return; }
            DialogResult = DialogResult.OK;
        };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(create);
        layout.Controls.Add(buttons, 1, 4);
        Controls.Add(layout);
        AcceptButton = create;
        CancelButton = cancel;
    }
}
