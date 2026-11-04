using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// エディターの画面 (ED3)
//   メニュー (シーンを開く / 保存) / Scene (シーンビュー。エディターのカメラで見る) / Hierarchy (親子のツリー) /
//   Inspector / Console (ログ) / Stats
//   シーンは編集中は動かさない (engine.IsPlaying = false。再生は ED4)
//   シーンビューの大きさでシーンを描く (Graphics.SetRenderSize)。窓の大きさを変えると描く大きさも変わる
// ==========================================================================
internal sealed class EditorApp
{
    private readonly Engine       _engine;
    private readonly ImGuiBackend _imgui;
    private readonly Inspector    _inspector;
    private readonly ConsoleLog   _log;
    private readonly string       _scenesDirectory;
    private World World => _engine.World;

    private Entity  _selected;
    private Entity  _dragged;
    private string? _scenePath;
    private bool    _layoutBuilt;
    private bool    _showHierarchy = true, _showInspector = true, _showConsole = true, _showStats = true;
    private bool    _resetLayout;

    // エディターのカメラ (シーンのカメラとは別。シーンには保存しない)
    private Vector3 _cameraPosition = new(0, 5, -10);
    private float   _yaw, _pitch = 20.0f;
    private float   _cameraSpeed = 8.0f;
    private bool    _sceneHovered;
    private int     _frames;
    private double  _frameMs;

    public EditorApp(Engine engine, ImGuiBackend imgui, ConsoleLog log, string scenesDirectory)
    {
        _engine = engine;
        _imgui  = imgui;
        _log    = log;
        _scenesDirectory = scenesDirectory;
        _inspector = new Inspector(engine);
        engine.LateUpdate += update;
    }

    public static string NameOf(World world, Entity entity) =>
        world.Has<EntityInfo>(entity) && !string.IsNullOrEmpty(world.Get<EntityInfo>(entity).Name) ? world.Get<EntityInfo>(entity).Name : $"Entity {entity.Id}";

    // ---------------------------------------------------------------------
    // シーンのファイル
    // ---------------------------------------------------------------------

    public void OpenScene(string path)
    {
        try
        {
            _engine.LoadScene(path);
            _scenePath = path;
            _selected  = Entity.Null;
            focusSceneCamera();
            _engine.Window.Text = $"GameEngine Editor - {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] Failed to open {path} : {ex.Message}");
        }
    }

    private void newScene()
    {
        World.Clear();
        _scenePath = null;
        _selected  = Entity.Null;
        var light = World.CreateEntity("Directional Light");
        World.Add(light, Transform.LookingAt(Vector3.Zero, new Vector3(0.4f, -1.0f, 0.6f)));
        World.Add(light, new DirectionalLight { Color = new Vector3(1.0f, 0.95f, 0.88f), Intensity = 1.6f });
        var camera = World.CreateEntity("Main Camera");
        World.Add(camera, Transform.LookingAt(new Vector3(0, 5, -10), Vector3.Zero));
        World.Add(camera, new Camera());
        _engine.Window.Text = "GameEngine Editor - (new scene)";
    }

    private void saveScene(bool saveAs)
    {
        var path = _scenePath;
        if (saveAs || path == null)
        {
            using var dialog = new SaveFileDialog { Filter = "Scene (*.scene.json)|*.scene.json", InitialDirectory = _scenesDirectory, FileName = "new.scene.json" };
            if (dialog.ShowDialog(_engine.Window) != DialogResult.OK) return;
            path = dialog.FileName;
        }
        _engine.SaveScene(path);
        _scenePath = path;
        _engine.Window.Text = $"GameEngine Editor - {Path.GetFileName(path)}";
        Console.WriteLine($"[Editor] Saved {path}");
    }

    private void openSceneDialog()
    {
        using var dialog = new OpenFileDialog { Filter = "Scene (*.scene.json)|*.scene.json|All files|*.*", InitialDirectory = _scenesDirectory };
        if (dialog.ShowDialog(_engine.Window) == DialogResult.OK) OpenScene(dialog.FileName);
    }

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

        updateCamera();
        _imgui.Render();
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
        DockBuilder.DockWindow("Scene", scene);
        DockBuilder.igDockBuilderFinish(dockspace);
    }

    private void drawMenu()
    {
        if (!ImGui.BeginMainMenuBar()) return;
        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Scene")) newScene();
            if (ImGui.MenuItem("Open Scene...", "Ctrl+O")) openSceneDialog();
            if (ImGui.MenuItem("Save Scene", "Ctrl+S")) saveScene(false);
            if (ImGui.MenuItem("Save Scene As...")) saveScene(true);
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) _engine.Quit();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("GameObject"))
        {
            if (ImGui.MenuItem("Create Empty")) createEntity(Entity.Null);
            if (ImGui.MenuItem("Delete", null, false, World.IsAlive(_selected))) deleteSelected();
            ImGui.EndMenu();
        }
        if (ImGui.BeginMenu("View"))
        {
            ImGui.MenuItem("Hierarchy", null, ref _showHierarchy);
            ImGui.MenuItem("Inspector", null, ref _showInspector);
            ImGui.MenuItem("Console",   null, ref _showConsole);
            ImGui.MenuItem("Stats",     null, ref _showStats);
            ImGui.Separator();
            var colliders = _engine.Physics.ShowColliders;
            if (ImGui.MenuItem("Show Colliders", null, ref colliders)) _engine.Physics.ShowColliders = colliders;
            var profiler = _engine.Graphics.ProfilerVisible;
            if (ImGui.MenuItem("Profiler (F1)", null, ref profiler)) _engine.Graphics.ProfilerVisible = profiler;
            ImGui.Separator();
            if (ImGui.MenuItem("Reset Layout")) _resetLayout = true;
            ImGui.EndMenu();
        }
        ImGui.TextDisabled(_scenePath != null ? $"   {Path.GetFileName(_scenePath)}" : "   (new scene)");
        ImGui.EndMainMenuBar();

        // ショートカット (文字の入力中でない時)
        var io = ImGui.GetIO();
        if (!io.WantTextInput && io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.S)) saveScene(false);
            if (ImGui.IsKeyPressed(ImGuiKey.O)) openSceneDialog();
        }
        if (!io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Delete) && World.IsAlive(_selected) && !_sceneHovered) deleteSelected();
    }

    // ---------------------------------------------------------------------
    // Scene (シーンビュー)
    // ---------------------------------------------------------------------

    private void drawScene()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.Begin("Scene"))
        {
            var size = ImGui.GetContentRegionAvail();
            var width  = Math.Max(1, (int)size.X);
            var height = Math.Max(1, (int)size.Y);
            _engine.Graphics.SetRenderSize(width, height);   // 変わった時だけ C++ が作り直す
            ImGui.Image((IntPtr)GraphicsSettings.ViewTexture, new Vector2(width, height));
            _sceneHovered = ImGui.IsItemHovered();
            // 操作の説明 (左上)
            var min = ImGui.GetItemRectMin();
            ImGui.GetWindowDrawList().AddText(min + new Vector2(8, 6), 0xC0FFFFFF, "Right drag : look / WASD QE : move / Shift : fast / Wheel : speed");
        }
        else
        {
            _sceneHovered = false;
        }
        ImGui.End();
        ImGui.PopStyleVar();
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
        var rotation = Quaternion.CreateFromYawPitchRoll(_yaw * MathEx.Deg2Rad, _pitch * MathEx.Deg2Rad, 0.0f);
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
        _engine.Graphics.CameraOverride = new CameraOverride
        {
            Position = _cameraPosition, Rotation = rotation, FieldOfView = 57.29578f, NearClip = 0.1f, FarClip = 10000.0f,
        };
    }
    private bool _flying;

    // ---------------------------------------------------------------------
    // Hierarchy (親子のツリー。ドラッグで親を変える / 右クリックで作る・消す)
    // ---------------------------------------------------------------------

    private readonly Dictionary<Entity, List<Entity>> _children = new();
    private readonly List<Entity> _roots = new();

    private void drawHierarchy()
    {
        if (ImGui.Begin("Hierarchy", ref _showHierarchy))
        {
            if (ImGui.Button("+ Create")) createEntity(Entity.Null);
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
            foreach (var root in _roots) drawNode(root);
            // 何も無い所へ落とすと親を外す
            ImGui.Dummy(new Vector2(-1, Math.Max(20.0f, ImGui.GetContentRegionAvail().Y)));
            if (ImGui.BeginDragDropTarget())
            {
                if (acceptEntity()) Hierarchy.SetParent(World, _dragged, Entity.Null);
                ImGui.EndDragDropTarget();
            }
            if (ImGui.IsItemClicked()) _selected = Entity.Null;
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
        if (inactive) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        var open = ImGui.TreeNodeEx($"{NameOf(World, entity)}##{entity.Id}", flags);
        if (inactive) ImGui.PopStyleColor();
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) _selected = entity;

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
            if (acceptEntity() && _dragged != entity)
            {
                if (!World.Has<Transform>(_dragged)) World.Add(_dragged, new Transform());
                if (!World.Has<Transform>(entity))   World.Add(entity, new Transform());
                if (!Hierarchy.SetParent(World, _dragged, entity)) Console.WriteLine("[Editor] Cannot make a cycle in the hierarchy.");
            }
            ImGui.EndDragDropTarget();
        }
        if (ImGui.BeginPopupContextItem())
        {
            _selected = entity;
            if (ImGui.MenuItem("Create Child")) createEntity(entity);
            if (ImGui.MenuItem("Delete")) deleteSelected();
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

    private void createEntity(Entity parent)
    {
        var entity = World.CreateEntity("Entity");
        World.Add(entity, new Transform { Parent = World.IsAlive(parent) && World.Has<Transform>(parent) ? parent : Entity.Null });
        _selected = entity;
    }

    private void deleteSelected()
    {
        if (!World.IsAlive(_selected)) return;
        Hierarchy.DestroyWithChildren(World, _selected);
        _selected = Entity.Null;
    }

    // ---------------------------------------------------------------------
    // Inspector / Stats
    // ---------------------------------------------------------------------

    private void drawInspectorWindow()
    {
        if (ImGui.Begin("Inspector", ref _showInspector)) _inspector.Draw(_selected);
        ImGui.End();
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
            ImGui.TextDisabled("Detailed GPU / CPU timings : View > Profiler (F1)");
        }
        ImGui.End();
    }
}
