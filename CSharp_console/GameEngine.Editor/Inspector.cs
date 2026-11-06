using System.Collections;
using System.Numerics;
using System.Reflection;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// インスペクター (ED3)。選んだエンティティのコンポーネントを、ED1 の型の情報 (ComponentRegistry) から自動で並べる
//   - 値の種類ごとの部品 : 数 (Range ならスライダー) / 真偽 / 文字列 / 列挙 (Flags はチェックの並び) / ベクトル (名前に Color が
//     入っていれば色) / 回転 (オイラー角の度) / エンティティ (一覧から選ぶ) / アセット (パス。Enter で読み込む) / 入れ子の struct / 配列
//   - 変えた値はその場でコンポーネントに書き戻す (次のフレームから効く)
//   - コンポーネントを足す (IComponent の一覧) / 外す (見出しの右クリック)
//   - 変更はすべて元に戻せる (ED4。UndoStack に「前の値 → 後の値」を積む。ドラッグ中の変更は 1 つにまとめる)
//   ※ユーザーの独自のインスペクターは ED7
// ==========================================================================
internal sealed class Inspector
{
    private readonly Engine _engine;
    private readonly UndoStack _undo;
    // 値を書いた時 (entity, 型, 前の JSON, 後の JSON)。エディターがプレハブの変えた値の印に使う (ED8)
    public Action<Entity, Type, string?, string>? Written;
    // コンポーネントの右クリック > Open Reference (ED9。スクリプトリファレンスで型を出す)
    public Action<Type>? OpenReference;
    private World World => _engine.World;
    private string _addFilter = "";
    private readonly Dictionary<(int, string), Vector3> _eulerCache = new();   // 回転をオイラー角で触っている間の値 (行き来でぶれないように)

    public Inspector(Engine engine, UndoStack undo)
    {
        _engine = engine;
        _undo   = undo;
        // 拡張 (ComponentEditor.DrawDefaultInspector) から既定のインスペクターを描けるように (ED7)
        EditorExtensions.DrawDefault = (ref object component, Entity owner) => drawDefault(ref component, ComponentRegistry.Get(component.GetType()), owner);
    }

    // 既定のインスペクター : 全部のフィールド (以前は Draw の中に書いていた。拡張からも使うので分けた。ED7)
    private bool drawDefault(ref object boxed, ComponentType type, Entity entity)
    {
        var changed = false;
        foreach (var field in type.Fields)
        {
            if (field.HideInInspector) continue;
            if (field.Header != null) ImGui.SeparatorText(field.Header);
            var value = field.GetValue(boxed);
            if (drawValue(field.Name, field.FieldType, ref value, field.Range, entity, field))
            {
                field.SetValue(boxed, value);
                changed = true;
            }
            if (field.Tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(field.Tooltip);
        }
        if (type.Fields.Count == 0) ImGui.TextDisabled("(no values)");
        return changed;
    }

    // コンポーネントの値を書いて、元に戻す記録を積む (before は書く前に取っておいた値)
    private void write(Entity entity, Type type, object boxed, string? before)
    {
        before ??= World.GetPool(type).Has(entity.Id) ? SceneSerializer.SaveComponent(World, entity, type) : null;
        EditorWorld.WriteComponent(World, entity, type, boxed);
        var id = EditorWorld.IdOf(World, entity);
        var after = SceneSerializer.SaveComponent(World, entity, type);
        _undo.Record(new ComponentEdit(id, type, before, after), (id, type));
        Written?.Invoke(entity, type, before, after);   // プレハブの置いた物なら、変えた値の印を付ける (ED8)
    }

    public void Draw(Entity entity)
    {
        if (!World.IsAlive(entity))
        {
            ImGui.TextDisabled("Nothing selected");
            return;
        }

        // ---------- エンティティ (名前 / 有効) ----------
        if (!World.Has<EntityInfo>(entity)) World.Add(entity, new EntityInfo($"Entity {entity.Id}"));
        var info = World.Get<EntityInfo>(entity);
        var active = info.Active;
        if (ImGui.Checkbox("##active", ref active)) write(entity, typeof(EntityInfo), info with { Active = active }, null);
        ImGui.SameLine();
        var name = info.Name ?? "";
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##name", ref name, 256)) write(entity, typeof(EntityInfo), info with { Name = name }, null);
        ImGui.TextDisabled($"Id {info.Id}");
        ImGui.Separator();

        // ---------- コンポーネント ----------
        IComponentPool? remove = null;
        foreach (var pool in World.Pools.ToList())
        {
            if (!pool.Has(entity.Id) || pool.ComponentType == typeof(EntityInfo) || pool.ComponentType == typeof(PrefabLink)) continue;   // PrefabLink は上の見出しで (ED8)
            var type = ComponentRegistry.Get(pool.ComponentType);
            ImGui.PushID(type.TypeName);
            var open = ImGui.CollapsingHeader(type.DisplayName, ImGuiTreeNodeFlags.DefaultOpen);
            if (ImGui.BeginPopupContextItem("component"))
            {
                if (ImGui.MenuItem("Remove Component")) remove = pool;
                if (ImGui.MenuItem("Open Reference")) OpenReference?.Invoke(pool.ComponentType);   // ED9
                ImGui.EndPopup();
            }
            if (open)
            {
                var boxed = pool.GetBoxed(entity.Id);
                bool changed;
                if (EditorExtensions.EditorFor(pool.ComponentType) is { } custom)
                {
                    // 独自のインスペクター (ED7)。例外はここで止めて、赤い字で出す (エディターは止めない)
                    custom.Entity = entity;
                    try { changed = custom.OnInspectorGUI(ref boxed); }
                    catch (Exception ex) { ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), $"{custom.GetType().Name} : {ex.Message}"); changed = false; }
                }
                else
                {
                    changed = drawDefault(ref boxed, type, entity);
                }
                if (changed) write(entity, pool.ComponentType, boxed, null);   // 以前は pool.SetBoxed (元に戻せず、Static な物の行列も作り直されなかった)
            }
            ImGui.PopID();
        }
        if (remove != null)
        {
            var id = EditorWorld.IdOf(World, entity);
            var before = SceneSerializer.SaveComponent(World, entity, remove.ComponentType);
            remove.Remove(entity.Id);
            _undo.Record(new ComponentEdit(id, remove.ComponentType, before, null));
        }

        // ---------- コンポーネントを足す ----------
        ImGui.Spacing();
        if (ImGui.Button("Add Component", new Vector2(-1, 0))) ImGui.OpenPopup("add");
        if (ImGui.BeginPopup("add"))
        {
            ImGui.SetNextItemWidth(240);
            ImGui.InputTextWithHint("##filter", "Search", ref _addFilter, 64);
            foreach (var type in ComponentRegistry.KnownComponents())
            {
                if (type.Hidden || World.GetPool(type.Type).Has(entity.Id)) continue;
                var label = string.IsNullOrEmpty(type.Category) ? type.DisplayName : $"{type.Category}/{type.DisplayName}";
                if (_addFilter.Length > 0 && !label.Contains(_addFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (ImGui.Selectable(label))
                {
                    World.GetPool(type.Type).SetBoxed(entity, type.CreateDefault());
                    var id = EditorWorld.IdOf(World, entity);
                    _undo.Record(new ComponentEdit(id, type.Type, null, SceneSerializer.SaveComponent(World, entity, type.Type)));
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }
    }

    // 値 1 つ分の部品。戻り値 : 変えたか
    private bool drawValue(string label, Type type, ref object? value, RangeAttribute? range, Entity owner, ComponentField? field = null)
    {
        // レイヤー (ED6) : 名前は ProjectSettings から
        if (field?.IsLayer == true && value is int layer) return drawLayer(label, ref value, layer);
        if (field?.IsLayerMask == true && value is uint mask) return drawLayerMask(label, ref value, mask);

        switch (value)
        {
        case float f:
            if (range != null ? ImGui.SliderFloat(label, ref f, range.Min, range.Max) : ImGui.DragFloat(label, ref f, 0.01f)) { value = f; return true; }
            return false;
        case double d:
            var df = (float)d;
            if (ImGui.DragFloat(label, ref df, 0.01f)) { value = (double)df; return true; }
            return false;
        case int i:
            if (range != null ? ImGui.SliderInt(label, ref i, (int)range.Min, (int)range.Max) : ImGui.DragInt(label, ref i)) { value = i; return true; }
            return false;
        case uint u:
            var ui = (int)Math.Min(u, int.MaxValue);
            if (u > int.MaxValue) { ImGui.LabelText(label, $"0x{u:X8}"); return false; }   // ビットの値 (CollideWith の全部など)
            if (ImGui.DragInt(label, ref ui, 1.0f, 0, int.MaxValue)) { value = (uint)ui; return true; }
            return false;
        case bool b:
            if (ImGui.Checkbox(label, ref b)) { value = b; return true; }
            return false;
        case string s:
            if (ImGui.InputText(label, ref s, 512)) { value = s; return true; }
            return false;
        case Enum e:
            return drawEnum(label, type, ref value, e);
        case Vector2 v2:
            if (ImGui.DragFloat2(label, ref v2, 0.01f)) { value = v2; return true; }
            return false;
        case Vector3 v3:
            if (label.Contains("Color") ? ImGui.ColorEdit3(label, ref v3, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR) : ImGui.DragFloat3(label, ref v3, 0.01f)) { value = v3; return true; }
            return false;
        case Vector4 v4:
            if (label.Contains("Color") ? ImGui.ColorEdit4(label, ref v4, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR) : ImGui.DragFloat4(label, ref v4, 0.01f)) { value = v4; return true; }
            return false;
        case Quaternion q:
            return drawRotation(label, ref value, q, owner);
        case Entity target:
            return drawEntity(label, ref value, target);
        case Guid g:
            ImGui.LabelText(label, g.ToString());
            return false;
        case Model model:
            return drawAssetPath(label, model.IsValid ? model.Path : "", ref value, path => _engine.Assets.LoadModel(path));
        case Sound sound:
            return drawAssetPath(label, sound.IsValid ? sound.Path : "", ref value, path => _engine.Assets.LoadSound(path));
        case Material material:
            return drawAssetPath(label, material.Path, ref value, path => _engine.Assets.LoadMaterial(path));
        case null when type == typeof(Material):
            return drawAssetPath(label, "", ref value, path => _engine.Assets.LoadMaterial(path));
        case CollisionMesh mesh:
            return drawAssetPath(label, mesh.Path, ref value, path => _engine.Assets.LoadCollisionMesh(_engine.Assets.LoadModel(path)));
        case null when type == typeof(CollisionMesh):
            return drawAssetPath(label, "", ref value, path => _engine.Assets.LoadCollisionMesh(_engine.Assets.LoadModel(path)));
        case IList list:
            return drawList(label, list, ref value, owner);
        }
        if (value != null && type.IsValueType)
        {
            // 入れ子の struct
            var changed = false;
            if (ImGui.TreeNode(label))
            {
                foreach (var inner in ComponentType.SerializedFields(type))
                {
                    if (inner.HideInInspector) continue;
                    if (inner.Header != null) ImGui.SeparatorText(inner.Header);
                    var innerValue = inner.GetValue(value);
                    if (drawValue(inner.Name, inner.FieldType, ref innerValue, inner.Range, owner, inner))
                    {
                        inner.SetValue(value, innerValue);   // value は箱に入れたコピーなので、そのまま書ける
                        changed = true;
                    }
                    if (inner.Tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(inner.Tooltip);
                }
                ImGui.TreePop();
            }
            return changed;
        }
        ImGui.LabelText(label, value?.ToString() ?? "(null)");
        return false;
    }

    private static bool drawEnum(string label, Type type, ref object? value, Enum current)
    {
        var names  = Enum.GetNames(type);
        var values = Enum.GetValues(type);
        if (type.IsDefined(typeof(FlagsAttribute), false))
        {
            // ビットの並び : 1 ビットの値だけチェックにする
            var bits = Convert.ToUInt64(current);
            var changed = false;
            if (ImGui.TreeNode($"{label} ({current})"))
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var bit = Convert.ToUInt64(values.GetValue(i));
                    if (bit == 0 || (bit & (bit - 1)) != 0) continue;
                    var on = (bits & bit) != 0;
                    if (ImGui.Checkbox(names[i], ref on))
                    {
                        bits = on ? bits | bit : bits & ~bit;
                        changed = true;
                    }
                }
                ImGui.TreePop();
            }
            if (changed) value = Enum.ToObject(type, bits);
            return changed;
        }
        var index = Array.IndexOf(values, current);
        if (ImGui.Combo(label, ref index, names, names.Length))
        {
            value = values.GetValue(index);
            return true;
        }
        return false;
    }

    // 回転 : オイラー角 (度。Y → X → Z の順に回す = Quaternion.CreateFromYawPitchRoll)
    private bool drawRotation(string label, ref object? value, Quaternion q, Entity owner)
    {
        var key = (owner.Id, label);
        if (!_eulerCache.TryGetValue(key, out var euler) || Quaternion.Dot(fromEuler(euler), q) is < 0.99999f and > -0.99999f) euler = toEuler(q);
        var edited = euler;
        if (ImGui.DragFloat3(label, ref edited, 0.5f))
        {
            _eulerCache[key] = edited;
            value = fromEuler(edited);
            return true;
        }
        _eulerCache[key] = euler;
        return false;
    }

    private static Quaternion fromEuler(Vector3 degrees) =>
        Quaternion.CreateFromYawPitchRoll(degrees.Y * MathEx.Deg2Rad, degrees.X * MathEx.Deg2Rad, degrees.Z * MathEx.Deg2Rad);

    private static Vector3 toEuler(Quaternion q)
    {
        // CreateFromYawPitchRoll の逆 (pitch = X, yaw = Y, roll = Z)
        var sinPitch = 2.0f * (q.W * q.X - q.Y * q.Z);
        float pitch = MathF.Abs(sinPitch) >= 1.0f ? MathF.CopySign(MathF.PI / 2, sinPitch) : MathF.Asin(sinPitch);
        float yaw   = MathF.Atan2(2.0f * (q.W * q.Y + q.X * q.Z), 1.0f - 2.0f * (q.X * q.X + q.Y * q.Y));
        float roll  = MathF.Atan2(2.0f * (q.W * q.Z + q.X * q.Y), 1.0f - 2.0f * (q.X * q.X + q.Z * q.Z));
        return new Vector3(pitch, yaw, roll) * MathEx.Rad2Deg;
    }

    // エンティティ : 一覧から選ぶ (None = 無し)
    private bool drawEntity(string label, ref object? value, Entity target)
    {
        var current = World.IsAlive(target) ? EditorApp.NameOf(World, target) : "None";
        var changed = false;
        if (ImGui.BeginCombo(label, current))
        {
            if (ImGui.Selectable("None", target.IsNull)) { value = Entity.Null; changed = true; }
            foreach (var e in World.Entities)
            {
                if (ImGui.Selectable($"{EditorApp.NameOf(World, e)}##{e.Id}", e == target)) { value = e; changed = true; }
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    // アセット : パス (Enter で読み込む。読めなければコンソールに出して元のまま)
    private static bool drawAssetPath<T>(string label, string path, ref object? value, Func<string, T> load)
    {
        var edited = path;
        var entered = ImGui.InputText(label, ref edited, 512, ImGuiInputTextFlags.EnterReturnsTrue);
        // Project からアセットを落とす (ED5。モデル / 当たりのメッシュはモデル、音は wav)
        if (ImGui.BeginDragDropTarget())
        {
            var kind = typeof(T) == typeof(Sound) ? AssetKind.Sound : typeof(T) == typeof(Material) ? AssetKind.Material : AssetKind.Model;
            if (ProjectPanel.AcceptDrop(kind) is { } asset) { edited = asset.Path; entered = true; }
            ImGui.EndDragDropTarget();
        }
        if (!entered || edited == path) return false;
        try
        {
            value = string.IsNullOrWhiteSpace(edited) ? default(T) : load(edited);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Inspector] {ex.Message}");
            return false;
        }
    }

    private bool drawList(string label, IList list, ref object? value, Entity owner)
    {
        var elementType = list.GetType().IsArray ? list.GetType().GetElementType()! : list.GetType().GetGenericArguments()[0];
        var changed = false;
        if (ImGui.TreeNode($"{label} ({list.Count})"))
        {
            var removeAt = -1;
            for (int i = 0; i < list.Count; i++)
            {
                ImGui.PushID(i);
                var item = list[i];
                if (drawValue($"[{i}]", elementType, ref item, null, owner))
                {
                    // 写しを書き換えて差し替える (以前はその場で書き換えていたので、元に戻す記録の「前の値」まで変わってしまった)
                    if (!changed) list = copyList(list);
                    list[i] = item;
                    changed = true;
                }
                // 消す (ED6)
                if (ImGui.BeginPopupContextItem("item"))
                {
                    if (ImGui.MenuItem("Remove")) removeAt = i;
                    ImGui.EndPopup();
                }
                ImGui.PopID();
            }
            // 足す / 消す (ED6) : 配列は 1 つ長い / 短い写しを作って差し替える
            if (ImGui.SmallButton("+ Add")) { list = resizeList(list, elementType, list.Count, insert: true); changed = true; }
            if (list.Count > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(right click an item to remove it)");
            }
            if (removeAt >= 0) { list = resizeList(list, elementType, removeAt, insert: false); changed = true; }
            ImGui.TreePop();
        }
        if (changed) value = list;
        return changed;
    }

    // 1 個足した (最後に既定の値) / 1 個消した写し
    private static IList resizeList(IList list, Type elementType, int index, bool insert)
    {
        var items = list.Cast<object?>().ToList();
        if (insert) items.Add(elementType == typeof(string) ? "" : elementType.IsValueType ? Activator.CreateInstance(elementType) : null);
        else items.RemoveAt(index);
        if (list is Array)
        {
            var array = Array.CreateInstance(elementType, items.Count);
            for (int i = 0; i < items.Count; i++) array.SetValue(items[i], i);
            return array;
        }
        var result = (IList)Activator.CreateInstance(list.GetType())!;
        foreach (var item in items) result.Add(item);
        return result;
    }

    // ---------------------------------------------------------------------
    // マテリアルのアセット (ED5)。値を変えるとすぐ C++ へ送り (同じマテリアルの物が全部変わる)、触り終わったらファイルに書く
    // ※アセットの変更は元に戻せない (シーンの変更ではないので UndoStack に積まない)
    // ---------------------------------------------------------------------
    private bool _materialDirty;
    private Material? _editingMaterial;

    public void DrawMaterial(Material material)
    {
        if (_editingMaterial != material) { saveMaterial(); _editingMaterial = material; }
        ImGui.Text(Path.GetFileName(material.Path));
        ImGui.TextDisabled(material.Path);
        ImGui.Separator();
        var changed = false;

        // ---------- 自分のシェーダー (SH2) ----------
        var shaderPath = material.Shader ?? "";
        var shaderEntered = ImGui.InputText("Shader", ref shaderPath, 512, ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.BeginDragDropTarget())
        {
            if (ProjectPanel.AcceptDrop(AssetKind.Shader, AssetKind.ShaderGraph) is { } asset) { shaderPath = asset.Path; shaderEntered = true; }   // SH4 : グラフも
            ImGui.EndDragDropTarget();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drop a shader (.hlsl) or a shader graph (.shadergraph.json) from Project (empty = the standard lit shader)");
        if (!string.IsNullOrEmpty(material.Shader))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("x##shader")) { shaderPath = ""; shaderEntered = true; }
        }
        if (shaderEntered) { material.Shader = string.IsNullOrWhiteSpace(shaderPath) ? null : shaderPath; changed = true; }
        if (material.ShaderAsset is { } shader)
        {
            if (shader.Error != null)
            {
                ImGui.PushTextWrapPos();
                ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.45f, 0.4f, 1.0f), shader.IsValid ? "Compile error (the previous version is used) :" : "Compile error :");
                ImGui.TextUnformatted(firstErrors(shader.Error));
                ImGui.PopTextWrapPos();
            }
            ImGui.TextDisabled($"{(shader.Transparent ? "transparent" : "opaque")} / blend {shader.Blend} / cull {shader.Cull} / zwrite {(shader.DepthWrite ? "on" : "off")}");
            changed |= drawShaderValues(material, shader);
            if (changed)
            {
                material.Apply();
                _materialDirty = true;
            }
            if (_materialDirty && !ImGui.IsAnyItemActive()) saveMaterial();
            return;   // シェーダーが色を決めるので、共通のシェーダーの値は出さない
        }
        ImGui.Separator();
        var color = material.Color;
        if (ImGui.ColorEdit4("Color", ref color, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.AlphaBar)) { material.Color = color; changed = true; }
        var texture = material.Texture ?? "";
        var entered = ImGui.InputText("Texture", ref texture, 512, ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.BeginDragDropTarget())
        {
            if (ProjectPanel.AcceptDrop(AssetKind.Texture) is { } asset) { texture = asset.Path; entered = true; }
            ImGui.EndDragDropTarget();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drop a texture from Project (empty = white)");
        if (entered) { material.Texture = string.IsNullOrWhiteSpace(texture) ? null : texture; changed = true; }
        var metallic = material.Metallic;
        if (ImGui.SliderFloat("Metallic", ref metallic, 0.0f, 1.0f)) { material.Metallic = metallic; changed = true; }
        var roughness = material.Roughness;
        if (ImGui.SliderFloat("Roughness", ref roughness, 0.02f, 1.0f)) { material.Roughness = roughness; changed = true; }
        var transparent = material.Transparent;
        if (ImGui.Checkbox("Transparent", ref transparent)) { material.Transparent = transparent; changed = true; }
        var doubleSided = material.DoubleSided;
        if (ImGui.Checkbox("Double Sided", ref doubleSided)) { material.DoubleSided = doubleSided; changed = true; }
        if (changed)
        {
            material.Apply();
            _materialDirty = true;
        }
        if (_materialDirty && !ImGui.IsAnyItemActive()) saveMaterial();
        ImGui.Spacing();
        ImGui.TextDisabled("Drag this material from Project onto an object (Scene / Hierarchy) to use it.");
    }

    // シェーダーのパラメーター (SH2)。値を変えたら true。既定の値に戻すのは右クリック
    private static bool drawShaderValues(Material material, ShaderAsset shader)
    {
        var changed = false;
        if (shader.Parameters.Count > 0) ImGui.SeparatorText("Parameters");
        foreach (var p in shader.Parameters)
        {
            ImGui.PushID(p.Name);
            material.ShaderValues.TryGetValue(p.Name, out var value);
            switch (p.Type)
            {
            case ShaderParamType.Float:
            {
                var f = value is float v ? v : p.DefaultValue.X;
                var edited = p.Range is { } r ? ImGui.SliderFloat(p.Name, ref f, r.X, r.Y) : ImGui.DragFloat(p.Name, ref f, 0.01f);
                if (edited) { material.ShaderValues[p.Name] = f; changed = true; }
                break;
            }
            case ShaderParamType.Color:
            {
                var c = value is System.Numerics.Vector4 v ? v : p.DefaultValue;
                if (ImGui.ColorEdit4(p.Name, ref c, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.HDR | ImGuiColorEditFlags.AlphaBar)) { material.ShaderValues[p.Name] = c; changed = true; }
                break;
            }
            case ShaderParamType.Vector:
            {
                var c = value is System.Numerics.Vector4 v ? v : p.DefaultValue;
                if (ImGui.DragFloat4(p.Name, ref c, 0.01f)) { material.ShaderValues[p.Name] = c; changed = true; }
                break;
            }
            default:
            {
                var path = value as string ?? p.DefaultTexture ?? "";
                var entered = ImGui.InputText(p.Name, ref path, 512, ImGuiInputTextFlags.EnterReturnsTrue);
                if (p.Type == ShaderParamType.Texture && ImGui.BeginDragDropTarget())
                {
                    if (ProjectPanel.AcceptDrop(AssetKind.Texture) is { } asset) { path = asset.Path; entered = true; }
                    ImGui.EndDragDropTarget();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(p.Type == ShaderParamType.Texture ? "Drop a texture (white = none)" : $"3D texture : builtin:{string.Join(" / builtin:", Noise3D.Kinds)} [:size]");
                if (entered) { material.ShaderValues[p.Name] = path; changed = true; }
                break;
            }
            }
            if (ImGui.BeginPopupContextItem("reset"))
            {
                if (ImGui.MenuItem("Reset to default")) { material.ShaderValues.Remove(p.Name); changed = true; }
                ImGui.EndPopup();
            }
            ImGui.PopID();
        }
        return changed;
    }

    // コンパイラーの文から、エラーの行を最大 6 行
    internal static string firstErrors(string error) =>
        string.Join("\n", error.Split('\n').Select(l => l.Trim()).Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains(".hlsl")).Take(6)) is { Length: > 0 } lines ? lines : error;

    /// <summary>シェーダーのアセット (SH2。Project で .hlsl を選んだ時) : 設定 / パラメーター / エラー</summary>
    // ---------------------------------------------------------------------
    // FBX のアセット (AN1) : 種類 / 読めたか / 中身の数 / 気を付ける事 / ノードの木 (中を見る)
    // ---------------------------------------------------------------------
    public void DrawFbx(GameEngine.Fbx.FbxAsset fbx)
    {
        ImGui.Text(Path.GetFileName(fbx.Path));
        ImGui.TextDisabled(fbx.Path);
        ImGui.Text($"{fbx.Type} ({GameEngine.Fbx.FbxAsset.ExtensionOf(fbx.Type)})   {fbx.FileBytes / 1024.0:F1} KB   read in {fbx.LoadMilliseconds:F1} ms");
        ImGui.Separator();
        if (fbx.Error != null)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.45f, 0.4f, 1.0f), "Cannot use this file :");
            ImGui.TextUnformatted(fbx.Error);
            ImGui.PopTextWrapPos();
        }
        else ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1.0f, 0.5f, 1.0f), $"OK : a {fbx.Type.ToString().ToLowerInvariant()}");
        if (fbx.Summary.Count > 0 && ImGui.BeginTable("##fbx", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Value");
            foreach (var (label, value) in fbx.Summary)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(label);
                ImGui.TableNextColumn(); ImGui.TextWrapped(value);
            }
            ImGui.EndTable();
        }
        foreach (var warning in fbx.Warnings) ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.8f, 0.3f, 1.0f), "! " + warning);
        if (fbx.Scene is { } scene && ImGui.CollapsingHeader($"Objects ({scene.Objects.Count})"))
        {
            foreach (var group in scene.Objects.Values.GroupBy(o => o.Kind).OrderBy(g => g.Key))
            {
                if (!ImGui.TreeNode($"{group.Key} ({group.Count()})##{group.Key}")) continue;
                foreach (var o in group.Take(200))
                    ImGui.BulletText($"{(o.Name.Length > 0 ? o.Name : "(no name)")}  {o.SubKind}  [{o.Children.Count} in / {o.Parents.Count} out]");
                ImGui.TreePop();
            }
        }
        if (fbx.Document is { } document && ImGui.CollapsingHeader("Raw nodes"))
            foreach (var node in document.Root.Children) drawFbxNode(node, 0);
    }

    private static void drawFbxNode(GameEngine.Fbx.FbxNode node, int depth)
    {
        string describe(object p) => p switch
        {
            string s => $"\"{(s.Length > 40 ? s[..40] + "..." : s).Replace("\0\x01", "::")}\"",
            Array a => $"{a.GetType().GetElementType()!.Name}[{a.Length}]",
            _ => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture) ?? "",
        };
        var label = $"{node.Name}  {string.Join(", ", node.Properties.Take(4).Select(describe))}{(node.Properties.Count > 4 ? " ..." : "")}##{node.Offset}";
        if (node.Children.Count == 0) { ImGui.BulletText(label[..label.IndexOf("##", StringComparison.Ordinal)]); return; }
        if (depth > 12 || !ImGui.TreeNode(label)) return;
        foreach (var child in node.Children.Take(500)) drawFbxNode(child, depth + 1);
        ImGui.TreePop();
    }

    // グラフの窓で開く (SH4。EditorApp が入れる)
    public Action<string>? OpenShaderGraph;

    public void DrawShader(ShaderAsset shader)
    {
        ImGui.Text(Path.GetFileName(shader.Path));
        ImGui.TextDisabled(shader.Path);
        if (shader.IsGraph && ImGui.Button("Open in Shader Graph")) OpenShaderGraph?.Invoke(shader.Path);   // SH4
        ImGui.Separator();
        if (shader.Error != null)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.45f, 0.4f, 1.0f), shader.IsValid ? "Compile error (the previous version is used) :" : "Compile error :");
            ImGui.TextUnformatted(firstErrors(shader.Error));
            ImGui.PopTextWrapPos();
        }
        else ImGui.TextColored(new System.Numerics.Vector4(0.5f, 1.0f, 0.5f, 1.0f), $"Compiled (version {shader.Version})");
        ImGui.Text($"Queue : {(shader.Transparent ? "transparent" : "opaque")}   Blend : {shader.Blend}   Cull : {shader.Cull}   ZWrite : {(shader.DepthWrite ? "on" : "off")}");
        if (shader.Parameters.Count > 0 && ImGui.BeginTable("##params", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Type");
            ImGui.TableSetupColumn("Default");
            ImGui.TableHeadersRow();
            foreach (var p in shader.Parameters)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(p.Name);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(p.Type.ToString());
                ImGui.TableNextColumn();
                ImGui.TextDisabled(p.IsTexture ? p.DefaultTexture ?? "" : p.Type == ShaderParamType.Float ? $"{p.DefaultValue.X:G4}" + (p.Range is { } r ? $"  [{r.X:G4} .. {r.Y:G4}]" : "")
                                   : $"({p.DefaultValue.X:G3}, {p.DefaultValue.Y:G3}, {p.DefaultValue.Z:G3}, {p.DefaultValue.W:G3})");
            }
            ImGui.EndTable();
        }
        ImGui.Spacing();
        ImGui.TextDisabled("Edit the file and save : it recompiles. Drop it onto a material's Shader slot to use it.");
        if (ImGui.Button("Open in Editor")) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(EditorApi.Assets.FullPath(shader.Path)) { UseShellExecute = true });
    }

    private void saveMaterial()
    {
        if (!_materialDirty || _editingMaterial == null) return;
        _editingMaterial.Save();
        _materialDirty = false;
    }

    // ---------------------------------------------------------------------
    // レイヤー (ED6)
    // ---------------------------------------------------------------------
    private static bool drawLayer(string label, ref object? value, int layer)
    {
        var settings = ProjectSettings.Current;
        var changed = false;
        if (ImGui.BeginCombo(label, $"{layer} : {settings.LayerName(layer)}"))
        {
            for (int i = 0; i < ProjectSettings.LayerCount; i++)
            {
                if (string.IsNullOrEmpty(settings.LayerNames[i]) && i != layer) continue;   // 名前の無いレイヤーは出さない
                if (ImGui.Selectable($"{i} : {settings.LayerName(i)}", i == layer)) { value = i; changed = true; }
            }
            ImGui.Separator();
            ImGui.TextDisabled("Name layers in Edit > Project Settings");
            ImGui.EndCombo();
        }
        return changed;
    }

    private static bool drawLayerMask(string label, ref object? value, uint mask)
    {
        var settings = ProjectSettings.Current;
        var preview = mask == 0xFFFFFFFFu ? "Everything" : mask == 0 ? "Nothing" : string.Join(", ",
            Enumerable.Range(0, ProjectSettings.LayerCount).Where(i => (mask & (1u << i)) != 0).Take(4).Select(settings.LayerName)) + (System.Numerics.BitOperations.PopCount(mask) > 4 ? ", ..." : "");
        var changed = false;
        if (ImGui.BeginCombo(label, preview))
        {
            if (ImGui.Selectable("Everything", mask == 0xFFFFFFFFu, ImGuiSelectableFlags.NoAutoClosePopups)) { mask = 0xFFFFFFFFu; changed = true; }
            if (ImGui.Selectable("Nothing", mask == 0, ImGuiSelectableFlags.NoAutoClosePopups)) { mask = 0; changed = true; }
            ImGui.Separator();
            for (int i = 0; i < ProjectSettings.LayerCount; i++)
            {
                var on = (mask & (1u << i)) != 0;
                if (string.IsNullOrEmpty(settings.LayerNames[i]) && !on) continue;
                if (ImGui.Checkbox($"{i} : {settings.LayerName(i)}", ref on)) { mask = on ? mask | (1u << i) : mask & ~(1u << i); changed = true; }
            }
            ImGui.EndCombo();
        }
        if (changed) value = mask;
        return changed;
    }

    // ---------------------------------------------------------------------
    // 普通のクラスの値 (ProjectSettings など。ED6)。戻り値 : 変えたか
    // ---------------------------------------------------------------------
    public bool DrawObject(object target)
    {
        var changed = false;
        foreach (var field in ComponentType.SerializedFields(target.GetType()))
        {
            if (field.HideInInspector) continue;
            if (field.Header != null) ImGui.SeparatorText(field.Header);
            var value = field.GetValue(target);
            if (drawValue(field.Name, field.FieldType, ref value, field.Range, Entity.Null, field))
            {
                field.SetValue(target, value);
                changed = true;
            }
            if (field.Tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(field.Tooltip);
        }
        return changed;
    }

    private static IList copyList(IList list) =>
        list is Array array ? (IList)array.Clone() : (IList)Activator.CreateInstance(list.GetType(), list)!;
}
