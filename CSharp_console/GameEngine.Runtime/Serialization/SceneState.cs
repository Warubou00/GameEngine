using System.Globalization;
using System.Numerics;
using System.Text;

namespace GameEngine;

// ==========================================================================
// 今のシーンの様子を文字にする / 2 つを比べる (ED8 後半)
//   書き出したゲーム (Player) とエディターの再生が同じ動きかを確かめるために使う
//   1 行目 : エンティティの数とコンポーネントの型ごとの数 (同じでないといけない)
//   2 行目から : 1 物 1 行 (Id / 名前 / 有効か / ワールドの位置 / 回転 / 大きさ)
//   シーンのファイルの物は Id で、ゲームのコードが作った物 (Id が毎回違う) は名前と位置の順で組にして比べる
// ==========================================================================
public static class SceneState
{
    public static string Dump(World world)
    {
        var builder = new StringBuilder();
        var counts = world.Pools.Where(p => p.Count > 0).Select(p => $"{p.ComponentType.FullName}={p.Count}").OrderBy(s => s, StringComparer.Ordinal);
        builder.Append("entities=").Append(world.EntityCount).Append(' ').AppendJoin(' ', counts).Append('\n');
        var lines = new List<string>();
        foreach (var entity in world.Entities)
        {
            var info = world.Has<EntityInfo>(entity) ? world.Get<EntityInfo>(entity) : default;
            var t = world.Has<Transform>(entity) ? world.Get<Transform>(entity) : new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One };
            var position = t.WorldPosition;
            var rotation = t.Parent.IsNull ? t.Rotation : Quaternion.CreateFromRotationMatrix(t.LocalToWorld);
            lines.Add(string.Join('\t', info.Id.ToString(), info.Name ?? "", world.IsActive(entity) ? "1" : "0",
                                  vector(position), vector(new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W)), vector(t.Scale)));
        }
        lines.Sort(StringComparer.Ordinal);
        foreach (var line in lines) builder.Append(line).Append('\n');
        return builder.ToString();
    }

    /// <summary>
    /// 2 つの Dump を比べる。戻り値 : 違う所の数 (数が違う / 物が見つからない / 位置が tolerance より離れている) と、一番大きい位置のずれ
    /// report には最初のいくつかの違いを書く
    /// </summary>
    public static (int Differences, float MaxError) Compare(string a, string b, float tolerance, List<string>? report = null)
    {
        var differences = 0;
        var maxError = 0.0f;
        void note(string message) { differences++; if (report != null && report.Count < 10) report.Add(message); }

        var linesA = a.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var linesB = b.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (linesA.Length == 0 || linesB.Length == 0) { note("empty dump"); return (differences, maxError); }
        if (linesA[0] != linesB[0]) note($"counts differ : '{linesA[0]}' vs '{linesB[0]}'");

        var rowsA = linesA.Skip(1).Select(parse).ToList();
        var rowsB = linesB.Skip(1).Select(parse).ToList();
        var byIdB = rowsB.GroupBy(r => r.Id).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
        var idsA  = rowsA.GroupBy(r => r.Id).Where(g => g.Count() == 1).Select(g => g.Key).ToHashSet();
        var restA = new List<Row>();
        var used  = new HashSet<string>();
        foreach (var row in rowsA)
        {
            if (idsA.Contains(row.Id) && byIdB.TryGetValue(row.Id, out var other)) { compare(row, other); used.Add(row.Id); }
            else restA.Add(row);
        }
        // Id で組にならなかった物 (ゲームのコードが作った物) : 名前ごとに位置の順で組にする
        var restB = rowsB.Where(r => !used.Contains(r.Id) || !idsA.Contains(r.Id)).ToList();
        foreach (var group in restA.GroupBy(r => r.Name))
        {
            var left  = group.OrderBy(r => r.Values[0]).ThenBy(r => r.Values[1]).ThenBy(r => r.Values[2]).ToList();
            var right = restB.Where(r => r.Name == group.Key).OrderBy(r => r.Values[0]).ThenBy(r => r.Values[1]).ThenBy(r => r.Values[2]).ToList();
            if (left.Count != right.Count) note($"'{group.Key}' : {left.Count} vs {right.Count} entities");
            for (int i = 0; i < Math.Min(left.Count, right.Count); i++) compare(left[i], right[i]);
        }
        return (differences, maxError);

        void compare(Row x, Row y)
        {
            if (x.Name != y.Name || x.Active != y.Active) { note($"{x.Name} ({x.Id}) : name / active differ"); return; }
            var error = 0.0f;
            for (int i = 0; i < x.Values.Length; i++) error = MathF.Max(error, MathF.Abs(x.Values[i] - y.Values[i]));
            // 回転は q と -q が同じ向き
            var flipped = 0.0f;
            for (int i = 0; i < x.Values.Length; i++) flipped = MathF.Max(flipped, MathF.Abs(x.Values[i] - (i is >= 3 and < 7 ? -y.Values[i] : y.Values[i])));
            error = MathF.Min(error, flipped);
            maxError = MathF.Max(maxError, error);
            if (error > tolerance) note($"{x.Name} ({x.Id}) : moved differently (error {error:G4})");
        }
    }

    private sealed record Row(string Id, string Name, string Active, float[] Values);

    private static Row parse(string line)
    {
        var parts = line.Split('\t');
        var values = parts.Skip(3).SelectMany(p => p.Split(' ')).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return new Row(parts[0], parts[1], parts[2], values);
    }

    private static string vector(Vector3 v) => FormattableString.Invariant($"{v.X:R} {v.Y:R} {v.Z:R}");
    private static string vector(Vector4 v) => FormattableString.Invariant($"{v.X:R} {v.Y:R} {v.Z:R} {v.W:R}");
}
