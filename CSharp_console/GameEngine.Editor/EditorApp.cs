using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// エディターの画面 (ED3 / ED4)
//   メニュー (シーンを開く / 保存、元に戻す) / Scene (シーンビュー。エディターのカメラで見る) / Hierarchy (親子のツリー) /
//   Inspector / Console (ログ) / Stats
//   シーンビューの大きさでシーンを描く (Graphics.SetRenderSize)。窓の大きさを変えると描く大きさも変わる
//   ED4 : シーンの編集
//     - クリックで選ぶ (Picker) / 選んだ物の箱 / ギズモ (W 移動 / E 回転 / R 拡大縮小、ローカル・ワールド、スナップ)
//     - 作る / 消す / 複製 (Ctrl+D) / 親を変える / コンポーネントを足す・外す / 値を変える → すべて元に戻せる (Ctrl+Z / Ctrl+Y)
//     - 再生 / 一時停止 / 1 フレーム進める / 停止 (停止で再生前のシーンに戻す。再生前のシーンは JSON で覚えておく)
//   ED5 : Project (アセットブラウザ)。モデルをシーンビュー / Hierarchy へドラッグして置く。シーンをシーンビューへ落とすと開く
//         マテリアルを物へドラッグすると付く / Project でマテリアルを選ぶとインスペクターで値を変えられる
//         ファイルが変わったら読み直す (モデル / テクスチャ / マテリアル / シェーダー)。Assets メニュー : 使っていない物の解放
//   編集中は engine.IsPlaying = false (ゲームの System / 物理 / 音を止める)
// ==========================================================================
internal sealed partial class EditorApp
{
    private readonly Engine       _engine;
    private readonly ImGuiBackend _imgui;
    private readonly Inspector    _inspector;
    private readonly ConsoleLog   _log;
    private readonly UndoStack    _undo = new();
    private readonly Picker       _picker;
    private readonly Gizmo        _gizmo = new();
    private readonly string       _scenesDirectory;
    private readonly AssetDatabase _database;
    private readonly ProjectPanel _project;
    private ShaderHotReload? _shaders;
    private readonly ScriptHost _scripts;   // ゲームのコード (ED7)
    private bool _inspectAsset;          // インスペクターに Project で選んだアセット (マテリアル) を出す
    private int  _seenAssetSelection;
    private World World => _engine.World;

    private Entity  _dragged;
    private string? _scenePath;
    private (EditCommand? Top, int Revision) _savedState;
    private bool    _layoutBuilt;
    private bool    _showHierarchy = true, _showInspector = true, _showConsole = true, _showStats = true, _showProject = true;
    private bool    _showProjectSettings;
    private readonly ProjectSettingsWindow _projectSettings;   // ED6
    private bool    _showBuild;
    private bool    _showReference;
    private readonly ShaderGraphWindow _graph;   // SH4
    private bool    _showGraph;
    private readonly ScriptReference _reference;   // スクリプトリファレンス (ED9)
    private readonly BuildWindow _build;   // ゲームの書き出し (ED8 後半)
    private bool    _resetLayout;

    // エディターのカメラ (シーンのカメラとは別。シーンには保存しない)
    private Vector3 _cameraPosition = new(0, 5, -10);
    private float   _yaw, _pitch = 20.0f;
    private float   _cameraSpeed = 8.0f;
    private const float FieldOfView = 57.29578f;
    private bool    _sceneHovered;
    private bool    _flying;
    private int     _frames;
    private double  _frameMs;

    // 再生
    private bool    _playing, _paused, _stepRequest, _stepping;
    private string? _playSnapshot;
    private bool    _gameView;   // シーンのカメラで見る (再生中の既定)

    // 自動の確認 (--editor-test)
    public Action<EditorApp>? FrameHook;

    public EditorApp(Engine engine, ImGuiBackend imgui, ConsoleLog log, AssetDatabase database, string scenesDirectory)
    {
        _engine = engine;
        _imgui  = imgui;
        _log    = log;
        _database = database;
        _scenesDirectory = scenesDirectory;
        Directory.CreateDirectory(scenesDirectory);
        _project = new ProjectPanel(engine, database, RequestOpenScene);   // 保存していない変更があれば先に聞く (ED10)
        database.Renamed += onAssetRenamed;   // ED10
        _project.PrefabChanged = SyncPrefab;   // ED8
        _scripts = new ScriptHost(engine, Path.Combine(database.ProjectRoot, "Scripts", "Game.csproj"));
        _reference = new ScriptReference(this, Path.Combine(database.ProjectRoot, "Scripts"));
        _graph = new ShaderGraphWindow(engine, database);   // SH4
        _project.OpenShaderGraph = path => OpenShaderGraph(path);
        _scripts.BuildSucceeded += _ => ReloadScripts();
        engine.Assets.AssetReloaded += _ => _picker.Clear();   // 形が変わったかもしれないので、クリックの当たりの形を捨てる
        _inspector = new Inspector(engine, _undo);
        _inspector.OpenShaderGraph = path => OpenShaderGraph(path);   // SH4
        _inspector.Written = MarkOverrides;   // プレハブの変えた値の印 (ED8)
        _inspector.OpenReference = type => OpenReference(type);   // コンポーネントの右クリック > Open Reference (ED9)
        EditorApi.App = this;   // 拡張から使う操作 (ED7)
        _projectSettings = new ProjectSettingsWindow(engine, _inspector, Path.Combine(database.ProjectRoot, ProjectSettings.FileName));
        engine.Window.FormClosing += onWindowClosing;   // 窓の × / Alt+F4 : 保存していない変更があれば聞く (ED10)
        _build = new BuildWindow(database, () => (_scenePath != null ? _database.ToAssetPath(_scenePath) : null, IsDirty));
        _picker = new Picker(engine);
        engine.LateUpdate += update;
    }

    public static string NameOf(World world, Entity entity) =>
        world.Has<EntityInfo>(entity) && !string.IsNullOrEmpty(world.Get<EntityInfo>(entity).Name) ? world.Get<EntityInfo>(entity).Name : $"Entity {entity.Id}";

    // ---------------------------------------------------------------------
    // 選んでいる物 (元に戻す / 再生の停止で番号が変わるので、Id でも覚えておく)
    // ---------------------------------------------------------------------

    private Entity _selected;
    private Guid   _selectedId;

    public Entity Selected
    {
        get => _selected;
        set
        {
            _selected   = World.IsAlive(value) ? value : Entity.Null;
            _inspectAsset = false;
            _selectedId = _selected.IsNull ? Guid.Empty : EditorWorld.IdOf(World, _selected);
        }
    }

    public Guid SelectedId => _selectedId;

    private void reselect() =>
        _selected = _selectedId == Guid.Empty ? Entity.Null : SceneSerializer.FindEntity(World, _selectedId);

    // ---------------------------------------------------------------------
    // シーンのファイル
    // ---------------------------------------------------------------------

    public void OpenScene(string path)
    {
        if (_playing) stop();
        try
        {
            _engine.LoadScene(path);
            var synced = SyncAllPrefabs();   // プレハブが変わっていれば置いた物に効かせる (ED8)
            if (synced > 0) Console.WriteLine($"[Prefab] Synced {synced} prefab instances");
            _scenePath = path;
            Selected   = Entity.Null;
            _undo.Clear();
            _savedState = _undo.State;
            focusSceneCamera();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] Failed to open {path} : {ex.Message}");
        }
    }

    private void newScene()
    {
        if (_playing) stop();
        World.Clear();
        _scenePath = null;
        Selected   = Entity.Null;
        _undo.Clear();
        _savedState = _undo.State;
        var light = World.CreateEntity("Directional Light");
        World.Add(light, Transform.LookingAt(Vector3.Zero, new Vector3(0.4f, -1.0f, 0.6f)));
        World.Add(light, new DirectionalLight { Color = new Vector3(1.0f, 0.95f, 0.88f), Intensity = 1.6f });
        var camera = World.CreateEntity("Main Camera");
        World.Add(camera, Transform.LookingAt(new Vector3(0, 5, -10), Vector3.Zero));
        World.Add(camera, new Camera());
    }

    public void SaveSceneTo(string path)
    {
        _engine.SaveScene(path);
        _scenePath = path;
        _savedState = _undo.State;
        Console.WriteLine($"[Editor] Saved {path}");
    }

    private void saveScene(bool saveAs)
    {
        if (_playing) { Console.WriteLine("[Editor] Stop playing before saving."); return; }
        var path = _scenePath;
        if (saveAs || path == null)
        {
            using var dialog = new SaveFileDialog { Filter = "Scene (*.scene.json)|*.scene.json", InitialDirectory = _scenesDirectory, FileName = "new.scene.json" };
            if (dialog.ShowDialog(_engine.Window) != DialogResult.OK) return;
            path = dialog.FileName;
        }
        SaveSceneTo(path);
    }

    private void openSceneDialog()
    {
        using var dialog = new OpenFileDialog { Filter = "Scene (*.scene.json)|*.scene.json|All files|*.*", InitialDirectory = _scenesDirectory };
        if (dialog.ShowDialog(_engine.Window) == DialogResult.OK) RequestOpenScene(dialog.FileName);   // 変更があれば聞く (ED10)
    }

    private void updateTitle()
    {
        var name = _scenePath != null ? Path.GetFileName(_scenePath) : "(new scene)";
        var dirty = _undo.State != _savedState ? "*" : "";
        var state = _playing ? (_paused ? " [Paused]" : " [Playing]") : "";
        var title = $"GameEngine Editor - {name}{dirty}{state}";
        if (title == _title) return;   // Window.Text を読むと毎フレーム Win32 を呼ぶので、自分で覚えた値と比べる
        _title = title;
        _engine.Window.Text = title;
    }
    private string _title = "";

    // シーンの最初のカメラの位置 / 向きから、エディターのカメラを始める
    private void focusSceneCamera()
    {
        foreach (var e in World.Entities)
        {
            if (!World.Has<Camera>(e) || !World.Has<Transform>(e)) continue;
            var t = World.Get<Transform>(e);
            _cameraPosition = t.WorldPosition;
            var f = t.Forward;
            _yaw   = MathF.Atan2(f.X, f.Z) * MathEx.Rad2Deg;
            _pitch = -MathF.Asin(Math.Clamp(f.Y, -1.0f, 1.0f)) * MathEx.Rad2Deg;
            return;
        }
    }

    // ---------------------------------------------------------------------
    // 毎フレーム
    // ---------------------------------------------------------------------

    private void update()
    {
        if (_stepping) { _engine.IsPlaying = false; _stepping = false; }   // 1 フレーム進めた後は止める
        if (!World.IsAlive(_selected)) _selected = Entity.Null;

        _frameMs = _frameMs * 0.95 + Time.DeltaTime * 1000.0 * 0.05;
        _imgui.NewFrame();
        drawMenu();
        var dockspace = ImGui.DockSpaceOverViewport();
        if (!_layoutBuilt || _resetLayout) buildLayout(dockspace);

        drawScene();
        if (_showHierarchy) drawHierarchy();
        if (_showInspector) drawInspectorWindow();
        if (_showConsole)   _log.Draw(ref _showConsole);
        if (_showStats)     drawStats();
        if (_showProject)   _project.Draw(ref _showProject);
        drawExtensionWindows();
        _projectSettings.Draw(ref _showProjectSettings);
        _build.Draw(ref _showBuild);
        drawUnsavedPrompt();   // ED10
        _reference.Draw(ref _showReference);
        _graph.Draw(ref _showGraph);   // SH4
        _shaders?.Update();
        _scripts.Update();

        handleShortcuts();
        updateCamera();
        FrameHook?.Invoke(this);
        _undo.EndFrame(ImGui.IsAnyItemActive() || _gizmo.Dragging);
        updateTitle();
        _imgui.Render();

        if (_stepRequest) { _engine.IsPlaying = true; _stepping = true; _stepRequest = false; }
        _frames++;
    }

    // 最初の並び : 左に Hierarchy、右に Inspector、下に Console / Stats、真ん中に Scene (imgui.ini があればその並び)
    private void buildLayout(uint dockspace)
    {
        _layoutBuilt = true;
        var iniExists = File.Exists(Path.Combine(AppContext.BaseDirectory, "imgui.ini"));
        if (iniExists && !_resetLayout) return;
        _resetLayout = false;
        DockBuilder.igDockBuilderRemoveNode(dockspace);
        DockBuilder.igDockBuilderAddNode(dockspace, DockBuilder.DockSpaceFlag);
        var (width, height) = _engine.ClientSize;
        DockBuilder.igDockBuilderSetNodeSize(dockspace, new Vector2(width, height));
        var (left, rest)    = DockBuilder.Split(dockspace, ImGuiDir.Left, 0.18f);
        var (right, center) = DockBuilder.Split(rest, ImGuiDir.Right, 0.26f);
        var (bottom, scene) = DockBuilder.Split(center, ImGuiDir.Down, 0.25f);
        DockBuilder.DockWindow("Hierarchy", left);
        DockBuilder.DockWindow("Inspector", right);
        DockBuilder.DockWindow("Console", bottom);
        DockBuilder.DockWindow("Stats", bottom);
        DockBuilder.DockWindow("Project", bottom);
        DockBuilder.DockWindow("Scene", scene);
        DockBuilder.igDockBuilderFinish(dockspace);
    }

    private void drawMenu()
    {
        if (!ImGui.BeginMainMenuBar()) return;
        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Scene")) RequestNewScene();   // 変更があれば聞く (ED10)
            if (ImGui.MenuItem("Open Scene...", "Ctrl+O")) openSceneDialog();
            if (ImGui.MenuItem("Save Scene", "Ctrl+S", false, !_playing)) saveScene(false);
            if (ImGui.MenuItem("Save Scene As...", null, false, !_playing)) saveScene(true);
            ImGui.Separator();
            ImGui.MenuItem("Build Game...", "Ctrl+B", ref _showBuild);   // ED8 後半
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) RequestQuit();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Edit"))
        {
            if (ImGui.MenuItem(_undo.UndoName is { } u ? $"Undo {u}" : "Undo", "Ctrl+Z", false, _undo.CanUndo)) Undo();
            if (ImGui.MenuItem(_undo.RedoName is { } r ? $"Redo {r}" : "Redo", "Ctrl+Y", false, _undo.CanRedo)) Redo();
            ImGui.Separator();
            if (ImGui.MenuItem("Duplicate", "Ctrl+D", false, World.IsAlive(_selected))) Duplicate();
            if (ImGui.MenuItem("Delete", "Del", false, World.IsAlive(_selected))) DeleteSelected();
            ImGui.Separator();
            if (ImGui.MenuItem(_playing ? "Stop" : "Play", "Ctrl+P")) TogglePlay();
            if (ImGui.MenuItem("Pause", null, _paused, _playing)) TogglePause();
            ImGui.Separator();
            ImGui.MenuItem("Project Settings...", null, ref _showProjectSettings);
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("GameObject"))
        {
            if (ImGui.MenuItem("Create Empty")) CreateEntity(Entity.Null);
            if (ImGui.MenuItem("Create Empty Child", null, false, World.IsAlive(_selected))) CreateEntity(_selected);
            // プリミティブ (SH1)。Shift を押しながら選ぶと、選んでいる物の子にする
            if (ImGui.BeginMenu("3D Object"))
            {
                foreach (var shape in Enum.GetValues<PrimitiveShape>())
                    if (ImGui.MenuItem(shape.ToString())) CreatePrimitive(shape, ImGui.GetIO().KeyShift ? _selected : Entity.Null);
                ImGui.EndMenu();
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Render Settings")) SelectRenderSettings();
            if (ImGui.MenuItem("Volume (Box)"))    CreateVolume(VolumeShape.Box);
            if (ImGui.MenuItem("Volume (Global)")) CreateVolume(VolumeShape.Global);
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Scripts"))
        {
            if (ImGui.MenuItem("Rebuild", null, false, !_scripts.IsBuilding)) _scripts.RequestBuild();
            if (ImGui.MenuItem("Open Scripts Folder")) System.Diagnostics.Process.Start("explorer.exe", $"\"{Path.Combine(_database.ProjectRoot, "Scripts")}\"");
            ImGui.Separator();
            ImGui.TextDisabled($"Game.dll #{_scripts.LoadCount}, {_scripts.SystemCount} systems, last build {_scripts.LastBuildMs:F0} ms");
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("Assets"))
        {
            if (ImGui.MenuItem("Create Material")) _project.CreateMaterial();
            if (ImGui.MenuItem("Reload Shaders", null, false, _shaders != null)) _shaders!.Reload();
            if (ImGui.MenuItem("Unload Unused Assets")) UnloadUnused();
            ImGui.EndMenu();
        }
        drawExtensionMenus();
        if (ImGui.BeginMenu("View"))
        {
            ImGui.MenuItem("Hierarchy", null, ref _showHierarchy);
            ImGui.MenuItem("Inspector", null, ref _showInspector);
            ImGui.MenuItem("Console",   null, ref _showConsole);
            ImGui.MenuItem("Stats",     null, ref _showStats);
            ImGui.MenuItem("Project",   null, ref _showProject);
            ImGui.MenuItem("Shader Graph", null, ref _showGraph);   // SH4
            ImGui.Separator();
            // 確認用の表示 (ED6。シーンには保存しない)
            if (ImGui.BeginMenu("Debug Display"))
            {
                var physics = _engine.Physics;
                var graphics = _engine.Graphics;
                var colliders = physics.ShowColliders;
                if (ImGui.MenuItem("Colliders", null, ref colliders)) physics.ShowColliders = colliders;
                var contacts = physics.ShowContacts;
                if (ImGui.MenuItem("Contacts", null, ref contacts)) physics.ShowContacts = contacts;
                var joints = physics.ShowJoints;
                if (ImGui.MenuItem("Joints", null, ref joints)) physics.ShowJoints = joints;
                ImGui.Separator();
                var cascades = graphics.Shadows.ShowCascades;
                if (ImGui.MenuItem("Shadow Cascades", null, ref cascades)) graphics.Shadows.ShowCascades = cascades;
                var ssaoOnly = graphics.Ssao.ShowOnly;
                if (ImGui.MenuItem("SSAO Only", null, ref ssaoOnly)) graphics.Ssao.ShowOnly = ssaoOnly;
                ImGui.EndMenu();
            }
            var profiler = _engine.Graphics.ProfilerVisible;
            if (ImGui.MenuItem("Profiler (F1)", null, ref profiler)) _engine.Graphics.ProfilerVisible = profiler;
            ImGui.Separator();
            if (ImGui.MenuItem("Reset Layout")) _resetLayout = true;
            ImGui.EndMenu();
        }
        // ヘルプ (ED9)
        if (ImGui.BeginMenu("Help"))
        {
            ImGui.MenuItem("Script Reference", "Ctrl+H", ref _showReference);
            ImGui.Separator();
            ImGui.TextDisabled($"GameEngine Editor {EditorVersion.Display}");
            ImGui.EndMenu();
        }
        ImGui.TextDisabled(_scenePath != null ? $"   {Path.GetFileName(_scenePath)}{(_undo.State != _savedState ? " *" : "")}" : "   (new scene)");
        // ゲームのコードの状態 (ED7)
        if (_scripts.IsBuilding) { ImGui.SameLine(); ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.3f, 1.0f), "   Compiling scripts..."); }
        else if (_scripts.LastError != null)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), "   Script errors (see Console)");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(_scripts.LastError);
        }
        ImGui.EndMainMenuBar();
    }

    // ショートカット (文字の入力中でない時。カメラを飛ばしている間の W / E / R は移動に使う)
    private void handleShortcuts()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput) return;
        if (_graph.HasFocus) return;   // シェーダーグラフの窓が自分で使う (元に戻す / 消す / 複製。SH4)
        if (io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.S)) saveScene(false);
            if (ImGui.IsKeyPressed(ImGuiKey.O)) openSceneDialog();
            if (ImGui.IsKeyPressed(ImGuiKey.Z)) { if (io.KeyShift) Redo(); else Undo(); }
            if (ImGui.IsKeyPressed(ImGuiKey.Y)) Redo();
            if (ImGui.IsKeyPressed(ImGuiKey.D)) Duplicate();
            if (ImGui.IsKeyPressed(ImGuiKey.P)) TogglePlay();
            if (ImGui.IsKeyPressed(ImGuiKey.B)) _showBuild = !_showBuild;   // ED8 後半
            if (ImGui.IsKeyPressed(ImGuiKey.H)) _showReference = !_showReference;   // ED9 (F1 は C++ のプロファイラーが使う)
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && World.IsAlive(_selected)) DeleteSelected();
        if (_flying) return;
        if (ImGui.IsKeyPressed(ImGuiKey.W)) _gizmo.Mode = GizmoMode.Translate;
        if (ImGui.IsKeyPressed(ImGuiKey.E)) _gizmo.Mode = GizmoMode.Rotate;
        if (ImGui.IsKeyPressed(ImGuiKey.R)) _gizmo.Mode = GizmoMode.Scale;
        if (ImGui.IsKeyPressed(ImGuiKey.F) && World.IsAlive(_selected)) FocusSelected();
    }

    // ---------------------------------------------------------------------
    // Scene (シーンビュー)
    // ---------------------------------------------------------------------

    private Quaternion cameraRotation => Quaternion.CreateFromYawPitchRoll(_yaw * MathEx.Deg2Rad, _pitch * MathEx.Deg2Rad, 0.0f);

    private void drawScene()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.Begin("Scene"))
        {
            ImGui.PopStyleVar();
            drawToolbar();
            var size = ImGui.GetContentRegionAvail();
            var width  = Math.Max(1, (int)size.X);
            var height = Math.Max(1, (int)size.Y);
            _engine.Graphics.SetRenderSize(width, height);   // 変わった時だけ C++ が作り直す
            ImGui.Image((IntPtr)GraphicsSettings.ViewTexture, new Vector2(width, height));
            _sceneHovered = ImGui.IsItemHovered();
            // Project からドラッグ : モデルはマウスの下 (当たった所 / 地面 / 10 m 先) に置く、シーンは開く
            if (ImGui.BeginDragDropTarget())
            {
                if (ProjectPanel.AcceptDrop(AssetKind.Model, AssetKind.Scene, AssetKind.Material, AssetKind.Prefab) is { } asset)
                {
                    var view = new SceneCamera(_cameraPosition, cameraRotation, FieldOfView, ImGui.GetItemRectMin(), new Vector2(width, height));
                    var ray = view.RayDirection(ImGui.GetIO().MousePos);
                    if (asset.Kind == AssetKind.Scene) RequestOpenScene(_database.FullPath(asset.Path));
                    else if (asset.Kind == AssetKind.Material) AssignMaterial(_picker.Pick(_cameraPosition, ray, out _), asset);   // マウスの下の物に付ける
                    else if (asset.Kind == AssetKind.Prefab) InstantiatePrefab(asset, dropPoint(ray), Entity.Null);              // ED8
                    else PlaceModel(asset, dropPoint(ray), Entity.Null);
                }
                ImGui.EndDragDropTarget();
            }
            var min = ImGui.GetItemRectMin();
            var draw = ImGui.GetWindowDrawList();
            if (_playing) draw.AddRect(min, min + new Vector2(width, height), _paused ? 0xFF20B0FFu : 0xFF3080FFu, 0, ImDrawFlags.None, 3.0f);   // 再生中は枠の色

            if (!_gameView)
            {
                var camera = new SceneCamera(_cameraPosition, cameraRotation, FieldOfView, min, new Vector2(width, height));
                drawSelection(camera, draw);
                drawVolume(camera, draw);
                drawExtensionGizmos(camera, draw);
                updateGizmo(camera, draw);
                // クリックで選ぶ (ギズモを掴んだ時 / カメラを回している時は選ばない)
                if (_sceneHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_gizmo.Dragging && !_flying)
                {
                    var mouse = ImGui.GetIO().MousePos;
                    Selected = _picker.Pick(_cameraPosition, camera.RayDirection(mouse), out _);
                }
                draw.AddText(min + new Vector2(8, 6), 0xC0FFFFFF, "Click : select / Right drag : look / WASD QE : move / W E R : gizmo / F : focus");
            }
            else
            {
                draw.AddText(min + new Vector2(8, 6), 0xC0FFFFFF, "Game camera");
            }
        }
        else
        {
            ImGui.PopStyleVar();
            _sceneHovered = false;
        }
        ImGui.End();
    }

    private void drawToolbar()
    {
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(6, 4));
        toolButton("Move", _gizmo.Mode == GizmoMode.Translate, () => _gizmo.Mode = GizmoMode.Translate);
        toolButton("Rotate", _gizmo.Mode == GizmoMode.Rotate, () => _gizmo.Mode = GizmoMode.Rotate);
        toolButton("Scale", _gizmo.Mode == GizmoMode.Scale, () => _gizmo.Mode = GizmoMode.Scale);
        ImGui.SameLine(0, 14);
        toolButton(_gizmo.Local ? "Local" : "World", false, () => _gizmo.Local = !_gizmo.Local);
        toolButton("Snap", _gizmo.Snap, () => _gizmo.Snap = !_gizmo.Snap);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Move {_gizmo.TranslateSnap} m / Rotate {_gizmo.RotateSnap} deg / Scale {_gizmo.ScaleSnap} (hold Ctrl while dragging also snaps)");

        // 再生のボタンは真ん中に
        var center = ImGui.GetWindowWidth() * 0.5f - 90.0f;
        ImGui.SameLine(Math.Max(center, ImGui.GetCursorPosX() + 14));
        toolButton(_playing ? "Stop" : "Play", _playing, TogglePlay);
        toolButton("Pause", _paused, TogglePause);
        toolButton("Step", false, Step);
        ImGui.SameLine(0, 14);
        toolButton(_gameView ? "Game Camera" : "Editor Camera", _gameView, () => _gameView = !_gameView);
        ImGui.NewLine();   // 最後のボタンの SameLine を終わらせる (シーンの画像は次の行)
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2);
    }

    private static void toolButton(string label, bool selected, Action onClick)
    {
        if (selected) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
        if (ImGui.Button(label)) onClick();
        if (selected) ImGui.PopStyleColor();
        ImGui.SameLine();
    }

    // 選んだ物の箱 (モデルの形の箱をワールドへ移して、12 本の線で描く)
    private void drawSelection(SceneCamera camera, ImDrawListPtr draw)
    {
        if (!World.IsAlive(_selected) || !World.Has<MeshRenderer>(_selected) || !World.Has<Transform>(_selected)) return;
        if (!_picker.TryGetBounds(World.Get<MeshRenderer>(_selected).Model, out var min, out var max)) return;
        var matrix = Hierarchy.GetWorldMatrix(World, _selected);
        Span<Vector2> points = stackalloc Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z);
            if (!camera.Project(Vector3.Transform(corner, matrix), out points[i])) return;
        }
        const uint color = 0xFF30A0FF;   // 橙
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) == 0) draw.AddLine(points[i], points[i | bit], color, 1.5f);
            }
        }
    }

    // 選んだ Volume の範囲 (箱 / 球の 3 つの輪)。BlendDistance の外側は薄く
    private void drawVolume(SceneCamera camera, ImDrawListPtr draw)
    {
        if (!World.IsAlive(_selected) || !World.Has<Volume>(_selected) || !World.Has<Transform>(_selected)) return;
        var volume = World.Get<Volume>(_selected);
        if (volume.Shape == VolumeShape.Global) return;
        Matrix4x4.Decompose(Hierarchy.GetWorldMatrix(World, _selected), out var scale, out var rotation, out var position);
        void box(Vector3 half, uint color)
        {
            Span<Vector2> points = stackalloc Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) != 0 ? half.X : -half.X, (i & 2) != 0 ? half.Y : -half.Y, (i & 4) != 0 ? half.Z : -half.Z);
                if (!camera.Project(position + Vector3.Transform(corner, rotation), out points[i])) return;
            }
            for (int i = 0; i < 8; i++)
                for (int bit = 1; bit < 8; bit <<= 1)
                    if ((i & bit) == 0) draw.AddLine(points[i], points[i | bit], color, 1.5f);
        }
        void sphere(float radius, uint color)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                Vector2 previous = default;
                var hasPrevious = false;
                for (int i = 0; i <= 48; i++)
                {
                    var a = i * MathF.Tau / 48;
                    var p = axis == 0 ? new Vector3(0, MathF.Cos(a), MathF.Sin(a)) : axis == 1 ? new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) : new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
                    var ok = camera.Project(position + p * radius, out var point);
                    if (ok && hasPrevious) draw.AddLine(previous, point, color, 1.5f);
                    previous = point;
                    hasPrevious = ok;
                }
            }
        }
        const uint inner = 0xFF60E060, outer = 0x8060E060;   // 緑 / 薄い緑
        if (volume.Shape == VolumeShape.Box)
        {
            var half = volume.Size * 0.5f * scale;
            box(half, inner);
            if (volume.BlendDistance > 0.0f) box(half + new Vector3(volume.BlendDistance), outer);
        }
        else
        {
            var radius = volume.Size.X * scale.X;
            sphere(radius, inner);
            if (volume.BlendDistance > 0.0f) sphere(radius + volume.BlendDistance, outer);
        }
    }

    private void updateGizmo(SceneCamera camera, ImDrawListPtr draw)
    {
        if (!World.IsAlive(_selected) || !World.Has<Transform>(_selected)) return;
        if (_gizmo.Update(camera, draw, World, _selected, _sceneHovered && !_flying, out var transform))
        {
            ref var t = ref World.Get<Transform>(_selected);
            if (t.Position == transform.Position && t.Rotation == transform.Rotation && t.Scale == transform.Scale) return;
            WriteTransform(_selected, transform.Position, transform.Rotation, transform.Scale);
        }
    }

    // Transform の位置 / 回転 / 大きさを変えて、元に戻す記録を積む (ドラッグ中は 1 つにまとめる)
    public void WriteTransform(Entity entity, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var id = EditorWorld.IdOf(World, entity);
        var before = SceneSerializer.SaveComponent(World, entity, typeof(Transform));
        ref var t = ref World.Get<Transform>(entity);
        t.Position = position;
        t.Rotation = rotation;
        t.Scale    = scale;
        t.MarkDirty();
        var after = SceneSerializer.SaveComponent(World, entity, typeof(Transform));
        _undo.Record(new ComponentEdit(id, typeof(Transform), before, after), (id, typeof(Transform)));
        MarkOverrides(entity, typeof(Transform), before, after);
    }

    // 選んだ物が画面に収まる所へカメラを動かす
    public void FocusSelected()
    {
        var matrix = Hierarchy.GetWorldMatrix(World, _selected);
        var center = matrix.Translation;
        var radius = 1.0f;
        if (World.Has<MeshRenderer>(_selected) && _picker.TryGetBounds(World.Get<MeshRenderer>(_selected).Model, out var min, out var max))
        {
            center = Vector3.Transform((min + max) * 0.5f, matrix);
            radius = Math.Max((Vector3.TransformNormal(max - min, matrix)).Length() * 0.5f, 0.1f);
        }
        var forward = Vector3.Transform(Vector3.UnitZ, cameraRotation);
        _cameraPosition = center - forward * (radius / MathF.Tan(FieldOfView * MathEx.Deg2Rad * 0.5f) * 1.3f);
    }

    // エディターのカメラ : シーンビューの上で右ボタンを押している間だけ動く (FreeCamera と同じ操作)
    private void updateCamera()
    {
        var flying = Input.GetMouseButton(MouseButtons.Right) && (_sceneHovered || _flying);
        _flying = flying;
        if (_sceneHovered && Input.ScrollDelta != 0.0f) _cameraSpeed = Math.Clamp(_cameraSpeed * MathF.Pow(1.2f, Input.ScrollDelta), 0.5f, 500.0f);
        if (flying)
        {
            var delta = Input.MouseDelta;
            _yaw   += delta.X * 0.2f;
            _pitch  = Math.Clamp(_pitch + delta.Y * 0.2f, -89.0f, 89.0f);
        }
        var rotation = cameraRotation;
        if (flying)
        {
            var move = Vector3.Zero;
            if (Input.GetKey(Keys.W)) move.Z += 1;
            if (Input.GetKey(Keys.S)) move.Z -= 1;
            if (Input.GetKey(Keys.D)) move.X += 1;
            if (Input.GetKey(Keys.A)) move.X -= 1;
            if (Input.GetKey(Keys.E)) move.Y += 1;
            if (Input.GetKey(Keys.Q)) move.Y -= 1;
            var speed = _cameraSpeed * (Input.GetKey(Keys.ShiftKey) ? 4.0f : 1.0f) * Time.DeltaTime;
            _cameraPosition += (Vector3.Transform(Vector3.UnitX, rotation) * move.X + Vector3.UnitY * move.Y + Vector3.Transform(Vector3.UnitZ, rotation) * move.Z) * speed;
        }
        _engine.Graphics.CameraOverride = _gameView ? null : new CameraOverride
        {
            Position = _cameraPosition, Rotation = rotation, FieldOfView = FieldOfView, NearClip = 0.1f, FarClip = 10000.0f,
        };
    }

    // ---------------------------------------------------------------------
    // Hierarchy (親子のツリー。ドラッグで親を変える / 右クリックで作る・消す)
    // ---------------------------------------------------------------------

    private readonly Dictionary<Entity, List<Entity>> _children = new();
    private readonly List<Entity> _roots = new();

    private void drawHierarchy()
    {
        if (ImGui.Begin("Hierarchy", ref _showHierarchy))
        {
            if (ImGui.Button("+ Create")) CreateEntity(Entity.Null);
            ImGui.SameLine();
            ImGui.TextDisabled($"{World.EntityCount} entities");
            ImGui.Separator();

            // 親 → 子の一覧 (毎フレーム作る。1 万個でも 1 ms かからない)
            foreach (var list in _children.Values) list.Clear();
            _roots.Clear();
            var transforms = World.GetPool<Transform>();
            foreach (var e in World.Entities)
            {
                var parent = transforms.Has(e.Id) ? transforms.Get(e.Id).Parent : Entity.Null;
                if (parent.IsNull || !World.IsAlive(parent)) { _roots.Add(e); continue; }
                if (!_children.TryGetValue(parent, out var list)) _children[parent] = list = new List<Entity>();
                list.Add(e);
            }
            ImGui.BeginChild("tree");
            // F2 : 選んでいる物の名前を書く (ED10。Hierarchy にフォーカスがある時)
            if (!_renaming.IsNull && !World.IsAlive(_renaming)) _renaming = Entity.Null;
            if (_renaming.IsNull && World.IsAlive(_selected) && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
                !ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F2, false))
                BeginRename(_selected);
            foreach (var root in _roots) drawNode(root);
            // 何も無い所へ落とすと親を外す
            ImGui.Dummy(new Vector2(-1, Math.Max(20.0f, ImGui.GetContentRegionAvail().Y)));
            if (ImGui.BeginDragDropTarget())
            {
                if (acceptEntity()) SetParent(_dragged, Entity.Null);
                if (ProjectPanel.AcceptDrop(AssetKind.Model, AssetKind.Prefab) is { } asset)
                {
                    if (asset.Kind == AssetKind.Prefab) InstantiatePrefab(asset, Vector3.Zero, Entity.Null);
                    else PlaceModel(asset, Vector3.Zero, Entity.Null);
                }
                ImGui.EndDragDropTarget();
            }
            if (ImGui.IsItemClicked()) Selected = Entity.Null;
            ImGui.EndChild();
        }
        ImGui.End();
    }

    private unsafe void drawNode(Entity entity)
    {
        var hasChildren = _children.TryGetValue(entity, out var children) && children.Count > 0;
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen;
        if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf;
        if (entity == _selected) flags |= ImGuiTreeNodeFlags.Selected;
        var inactive = !World.IsActive(entity);
        var prefab = World.Has<PrefabLink>(entity);   // プレハブの置いた物は青 (ED8)
        if (inactive) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        else if (prefab) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.75f, 1.0f, 1.0f));
        // 名前を書いている物 (ED10) : 名前の代わりに入力の欄
        var renaming = entity == _renaming;
        var open = ImGui.TreeNodeEx(renaming ? $"##{entity.Id}" : $"{NameOf(World, entity)}##{entity.Id}", flags);
        if (inactive || prefab) ImGui.PopStyleColor();
        if (renaming)
        {
            ImGui.SameLine();
            drawRenameField(entity);
        }
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) Selected = entity;
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { Selected = entity; FocusSelected(); }

        // ドラッグで親を変える
        if (ImGui.BeginDragDropSource())
        {
            _dragged = entity;
            int dummy = entity.Id;
            ImGui.SetDragDropPayload("ENTITY", (IntPtr)(&dummy), sizeof(int));
            ImGui.Text(NameOf(World, entity));
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginDragDropTarget())
        {
            if (acceptEntity() && _dragged != entity) SetParent(_dragged, entity);
            if (ProjectPanel.AcceptDrop(AssetKind.Model, AssetKind.Material, AssetKind.Prefab) is { } asset)
            {
                if (asset.Kind == AssetKind.Material) AssignMaterial(entity, asset);
                else if (asset.Kind == AssetKind.Prefab) InstantiatePrefab(asset, Hierarchy.GetWorldMatrix(World, entity).Translation, entity);   // 子として (ED8)
                else PlaceModel(asset, Hierarchy.GetWorldMatrix(World, entity).Translation, entity);   // 子として親の位置に
            }
            ImGui.EndDragDropTarget();
        }
        if (ImGui.BeginPopupContextItem())
        {
            Selected = entity;
            if (ImGui.MenuItem("Rename", "F2")) BeginRename(entity);   // ED10
            if (ImGui.MenuItem("Create Child")) CreateEntity(entity);
            if (ImGui.BeginMenu("Create 3D Object Child"))   // SH1
            {
                foreach (var shape in Enum.GetValues<PrimitiveShape>())
                    if (ImGui.MenuItem(shape.ToString())) CreatePrimitive(shape, entity);
                ImGui.EndMenu();
            }
            // プレハブ (ED8)
            if (PrefabRootOf(entity).IsNull)
            {
                if (ImGui.MenuItem("Create Prefab", null, false, !World.Has<PrefabLink>(entity))) CreatePrefab(entity);
            }
            else if (ImGui.BeginMenu("Prefab"))
            {
                if (ImGui.MenuItem("Apply")) ApplyPrefab(entity);
                if (ImGui.MenuItem("Revert")) RevertPrefab(entity);
                if (ImGui.MenuItem("Unpack")) UnpackPrefab(entity);
                ImGui.EndMenu();
            }
            if (ImGui.MenuItem("Duplicate")) Duplicate();
            if (ImGui.MenuItem("Delete")) DeleteSelected();
            ImGui.EndPopup();
        }
        if (open)
        {
            if (hasChildren) foreach (var child in children!.ToList()) drawNode(child);
            ImGui.TreePop();
        }
    }

    private static unsafe bool acceptEntity()
    {
        var payload = ImGui.AcceptDragDropPayload("ENTITY");
        return payload.NativePtr != null;
    }

    // ---------------------------------------------------------------------
    // Inspector / Stats
    // ---------------------------------------------------------------------

    private void drawInspectorWindow()
    {
        // Project でマテリアルを選んだら、それを出す (Hierarchy / シーンビューで物を選ぶと物に戻る)
        if (_project.SelectionVersion != _seenAssetSelection)
        {
            _seenAssetSelection = _project.SelectionVersion;
            _inspectAsset = _project.Selected?.Kind is AssetKind.Material or AssetKind.Shader or AssetKind.ShaderGraph;   // SH2 : シェーダーも / SH4 : グラフも
        }
        if (ImGui.Begin("Inspector", ref _showInspector))
        {
            if (_inspectAsset && _project.Selected is { Kind: AssetKind.Shader or AssetKind.ShaderGraph } shaderEntry)
            {
                try { _inspector.DrawShader(_engine.Assets.LoadShader(shaderEntry.Path)); }
                catch (Exception ex) { ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), ex.Message); }
            }
            else if (_inspectAsset && _project.Selected is { Kind: AssetKind.Material } entry)
            {
                try { _inspector.DrawMaterial(_engine.Assets.LoadMaterial(entry.Path)); }
                catch (Exception ex) { ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), ex.Message); }
            }
            else
            {
                drawPrefabHeader();
                _inspector.Draw(_selected);
            }
        }
        ImGui.End();
    }

    // ---------------------------------------------------------------------
    // 拡張 (ED7) : メニュー / 窓 / ギズモ。拡張の中の例外は止めて Console に出す (エディターは止めない)
    // ---------------------------------------------------------------------

    private readonly HashSet<string> _reportedErrors = new();

    private void reportOnce(string where, Exception ex)
    {
        if (_reportedErrors.Add($"{where}:{ex.Message}")) Console.WriteLine($"[Editor] {where} : {(ex.InnerException ?? ex).Message}");
    }

    // メニューの道 ("Game/Tools/Do") を順に開く。最後の名前を返す (開けなければ null)
    private static string? openMenuPath(string path, out int opened)
    {
        var parts = path.Split('/');
        opened = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!ImGui.BeginMenu(parts[i])) return null;
            opened++;
        }
        return parts[^1];
    }

    private static void closeMenus(int opened)
    {
        for (int i = 0; i < opened; i++) ImGui.EndMenu();
    }

    private void drawExtensionMenus()
    {
        foreach (var (path, method) in EditorExtensions.MenuItems.ToList())
        {
            var leaf = openMenuPath(path, out var opened);
            if (leaf != null && ImGui.MenuItem(leaf))
            {
                try { method.Invoke(null, null); }
                catch (Exception ex) { reportOnce(path, ex); }
            }
            closeMenus(opened);
        }
        foreach (var (path, window) in EditorExtensions.Windows)
        {
            var leaf = openMenuPath(path, out var opened);
            if (leaf != null && ImGui.MenuItem(leaf, null, window.IsOpen)) window.IsOpen = !window.IsOpen;
            closeMenus(opened);
        }
    }

    private void drawExtensionWindows()
    {
        foreach (var (path, window) in EditorExtensions.Windows)
        {
            if (!window.IsOpen) continue;
            var open = true;
            ImGui.SetNextWindowSize(new Vector2(360, 300), ImGuiCond.FirstUseEver);
            if (ImGui.Begin($"{window.Title}##{path}", ref open))
            {
                try { window.OnGUI(); }
                catch (Exception ex) { ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), ex.Message); reportOnce(path, ex); }
            }
            ImGui.End();
            window.IsOpen = open;
        }
    }

    public int LastGizmoCount { get; private set; }   // 拡張が描いたギズモの数 (確認用)

    private void drawExtensionGizmos(SceneCamera camera, ImDrawListPtr draw)
    {
        if (EditorExtensions.Editors.Count == 0) { LastGizmoCount = 0; return; }
        var gizmos = new Gizmos(camera, draw);
        foreach (var (type, editor) in EditorExtensions.Editors)
        {
            var pool = World.GetPool(type);
            if (pool.Count == 0) continue;
            foreach (var entity in World.Entities)
            {
                if (!pool.Has(entity.Id) || !World.IsActive(entity)) continue;
                editor.Entity = entity;
                try
                {
                    var component = pool.GetBoxed(entity.Id);
                    editor.OnDrawGizmos(gizmos, component);
                    if (entity == _selected) editor.OnDrawGizmosSelected(gizmos, component);
                }
                catch (Exception ex) { reportOnce($"{editor.GetType().Name}.OnDrawGizmos", ex); }
            }
        }
        LastGizmoCount = gizmos.Count;
    }

    // 選んだ物がプレハブの置いた物なら、プレハブの名前と Apply / Revert / Unpack (ED8)
    private void drawPrefabHeader()
    {
        if (!World.IsAlive(_selected) || !World.Has<PrefabLink>(_selected)) return;
        var root = PrefabRootOf(_selected);
        var link = World.Get<PrefabLink>(_selected);
        var prefab = _engine.Assets.LoadPrefab(link.Prefab);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.75f, 1.0f, 1.0f));
        ImGui.Text($"Prefab : {(prefab != null ? Path.GetFileName(prefab.Path) : "(missing)")}");
        ImGui.PopStyleColor();
        var count = link.Overrides?.Length ?? 0;
        ImGui.SameLine();
        ImGui.TextDisabled(count > 0 ? $"({count} overrides)" : "(no overrides)");
        if (count > 0 && ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n", link.Overrides!));
        if (!root.IsNull)
        {
            if (ImGui.SmallButton("Apply")) ApplyPrefab(_selected);
            ImGui.SameLine();
            if (ImGui.SmallButton("Revert")) RevertPrefab(_selected);
            ImGui.SameLine();
            if (ImGui.SmallButton("Unpack")) UnpackPrefab(_selected);
            ImGui.SameLine();
            if (ImGui.SmallButton("Select Root")) Selected = root;
        }
        ImGui.Separator();
    }

    private void drawStats()
    {
        if (ImGui.Begin("Stats", ref _showStats))
        {
            var (rw, rh) = _engine.Graphics.RenderSize;
            ImGui.Text($"Frame {_frameMs:F2} ms ({1000.0 / Math.Max(_frameMs, 0.001):F0} FPS)");
            ImGui.Text($"C# update {_engine.UpdateMilliseconds:F3} ms");
            ImGui.Text($"Scene view {rw} x {rh}");
            ImGui.Text($"Entities {World.EntityCount}");
            ImGui.Text($"Physics bodies {_engine.Physics.Bodies}, rigid {_engine.Physics.RigidBodies}");
            var particles = _engine.Graphics.Particles;
            ImGui.Text($"Particles {particles.AliveCount} alive, {particles.VisibleCount} visible, {particles.EmitterCount} emitters");
            ImGui.Text($"Undo {_undo.Count}{(_undo.UndoName is { } name ? $" (last : {name})" : "")}");
            ImGui.Text($"Models {_engine.Graphics.LoadedModelCount}, materials {_engine.Assets.LoadedMaterials.Count()}, mesh memory {_engine.Graphics.MeshBytesUsed / (1024.0 * 1024.0):F2} MB");
            ImGui.Text($"Asset reloads {_engine.Assets.ReloadCount}, shader reloads {_shaders?.ReloadCount ?? 0}");
            ImGui.TextDisabled("Detailed GPU / CPU timings : View > Profiler (F1)");
        }
        ImGui.End();
    }
}
