using System.Drawing;
using System.Reflection;

namespace GameEngine;

// ==========================================================================
// エンジンの名前 / 版 / アイコン (Package 化で足した)
//   名前と版は ../Engine.props (csproj の Product / Version)。窓のタイトルやエディター / ランチャーの表示に使う
//   アイコン : 動いている exe に付いているアイコン (エディター / Player の exe は Engine.props の ApplicationIcon)
// ==========================================================================
public static class EngineInfo
{
    private static readonly Assembly Self = typeof(EngineInfo).Assembly;

    /// <summary>エンジンの名前 ("Tsumugi Engine")</summary>
    public static string Name { get; } = Self.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "GameEngine";

    /// <summary>エンジンの版 ("1.0.0.0"。git のコミットは付けない)</summary>
    public static string Version { get; } = (Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>動いている exe のアイコン (窓の左上 / タスクバー。読めなければ null = WinForms の既定)</summary>
    public static Icon? ExeIcon()
    {
        try { return Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null; }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return null; }
    }
}
