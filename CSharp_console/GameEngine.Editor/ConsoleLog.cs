using System.Numerics;
using System.Text;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// Console (ED3)。Console.WriteLine (エンジンと C++ のログも) を写して、エディターの窓に出す
//   元のコンソールにもそのまま書く。C++ のログは別のスレッドから来ることがあるので鍵をかける
// ==========================================================================
internal sealed class ConsoleLog : TextWriter
{
    private const int MaxLines = 2000;
    private readonly TextWriter _original;
    private readonly List<string> _lines = new();
    private readonly StringBuilder _current = new();
    private readonly object _lock = new();
    private string _filter = "";
    private bool _scrollToBottom = true;

    public ConsoleLog(TextWriter original) => _original = original;

    public override Encoding Encoding => _original.Encoding;

    public override void Write(char value)
    {
        _original.Write(value);
        lock (_lock)
        {
            if (value == '\n') { push(_current.ToString().TrimEnd('\r')); _current.Clear(); }
            else _current.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (value == null) return;
        foreach (var c in value) Write(c);
    }

    private void push(string line)
    {
        _lines.Add(line);
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        _scrollToBottom = true;
    }

    public void Draw(ref bool open)
    {
        if (ImGui.Begin("Console", ref open))
        {
            if (ImGui.Button("Clear")) lock (_lock) _lines.Clear();
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##filter", "Filter", ref _filter, 64);
            ImGui.Separator();
            ImGui.BeginChild("lines", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
            lock (_lock)
            {
                foreach (var line in _lines)
                {
                    if (_filter.Length > 0 && !line.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
                    var error = line.Contains("Failed", StringComparison.OrdinalIgnoreCase) || line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("FAIL");
                    var warning = line.Contains("warning", StringComparison.OrdinalIgnoreCase) || line.Contains("not found", StringComparison.OrdinalIgnoreCase);
                    if (error) ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), line);
                    else if (warning) ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.4f, 1.0f), line);
                    else ImGui.TextUnformatted(line);
                }
                if (_scrollToBottom && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 40.0f) ImGui.SetScrollHereY(1.0f);
                _scrollToBottom = false;
            }
            ImGui.EndChild();
        }
        ImGui.End();
    }
}
