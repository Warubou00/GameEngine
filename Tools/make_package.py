# -*- coding: utf-8 -*-
# ==========================================================================
# Package を作る (Visual Studio / .NET / DirectX の SDK を入れていない PC へ持っていくためのフォルダ)
#   python Tools/make_package.py [--skip-build] [--out <フォルダ>]
#     --skip-build : ビルドしない (前にビルドした物を写すだけ)
#     --out        : 書き出し先 (既定はリポジトリの Package)。中の Luncher (Data 以外) / Editor は作り直す
#                    (Project の中は消さない。--fresh-project で SampleProject だけ写し直す)
#
#   Package/
#     Luncher/                        ランチャー (GameEngine.Launcher.exe) + dotnet (.NET の動かすだけの物)
#     Editor/<エンジン名>_v<版>/      Editor (エディター) / Shader / Player (ゲームの書き出しに使う) / dotnet (.NET SDK : ゲームのコードのビルド)
#     Project/SampleProject/          見本のプロジェクト (CSharp_console/Project の Assets / Scripts / 設定)
#     README.txt
#   エンジンの名前と版は CSharp_console/Engine.props (EngineName / EngineVersion)
#   .NET は、この PC に入っている物 (C:\Program Files\dotnet) から要る分だけ写す (ダウンロードはしない)
# ==========================================================================
import argparse
import glob
import os
import re
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CS = os.path.join(ROOT, "CSharp_console")
DOTNET_ROOT = os.path.join(os.environ.get("ProgramW6432", r"C:\Program Files"), "dotnet")


def engine_props():
    text = open(os.path.join(CS, "Engine.props"), encoding="utf-8").read()
    name = re.search(r"<EngineName>(.*?)</EngineName>", text).group(1)
    version = re.search(r"<EngineVersion>(.*?)</EngineVersion>", text).group(1)
    return name, version


def run(args, **kw):
    print(">", " ".join(args), flush=True)
    result = subprocess.run(args, **kw)
    if result.returncode != 0:
        sys.exit(f"failed : {' '.join(args)}")


def msbuild():
    vswhere = os.path.join(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"), r"Microsoft Visual Studio\Installer\vswhere.exe")
    out = subprocess.run([vswhere, "-latest", "-find", r"MSBuild\**\Bin\MSBuild.exe"], capture_output=True, text=True).stdout.splitlines()   # v1.6 : 行ごと (以前は split() で "C:\Program Files" が切れた)
    if not out:
        sys.exit("MSBuild not found")
    return out[0]


def build():
    ms = msbuild()
    # C++ : 各プロジェクトの x64/Release に出す (NativeEngine.targets がそこから写す)
    for project in ["Core/Core", "RHI/RHI", "Renderer/Renderer", "Audio/Audio", "Physics/Physics"]:
        run([ms, os.path.join(ROOT, project + ".vcxproj"), "/t:Build", "/p:Configuration=Release", "/p:Platform=x64", "/m", "/v:minimal", "/nologo"])
    # C# : エディター / ランチャー (x64) と Player (GameBuilder と同じ AnyCPU の出力)
    for project, extra in [("GameEngine.Editor/GameEngine.Editor.csproj", ["-p:Platform=x64"]),
                           ("GameEngine.Launcher/GameEngine.Launcher.csproj", ["-p:Platform=x64"]),
                           ("GameEngine.Player/GameEngine.Player.csproj", [])]:
        run(["dotnet", "build", os.path.join(CS, project), "-c", "Release", "-nologo", "-v", "q", "-clp:NoSummary"] + extra)


def copy_tree(src, dst, skip=lambda rel: False):
    count = 0
    for folder, dirs, files in os.walk(src):
        rel_folder = os.path.relpath(folder, src)
        dirs[:] = [d for d in dirs if not skip(os.path.normpath(os.path.join(rel_folder, d)) + os.sep)]
        for f in files:
            rel = os.path.normpath(os.path.join(rel_folder, f))
            if skip(rel):
                continue
            target = os.path.join(dst, rel)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copy2(os.path.join(folder, f), target)
            count += 1
    return count


def newest(folder, major):
    # folder の下の "<major>.x.y" のフォルダの一番新しい版
    versions = [d for d in os.listdir(folder) if re.fullmatch(rf"{major}\.\d+\.\d+", d)]
    if not versions:
        sys.exit(f"no {major}.x in {folder}")
    return max(versions, key=lambda v: tuple(int(x) for x in v.split(".")))


def copy_dotnet(target, sdk):
    # .NET : 動かすだけの物 (host/fxr + Microsoft.NETCore.App + Microsoft.WindowsDesktop.App)。sdk=True なら SDK と参照用のパックも
    runtime = newest(os.path.join(DOTNET_ROOT, "shared", "Microsoft.NETCore.App"), 9)
    desktop = newest(os.path.join(DOTNET_ROOT, "shared", "Microsoft.WindowsDesktop.App"), 9)
    fxr = newest(os.path.join(DOTNET_ROOT, "host", "fxr"), 9)
    parts = [("host/fxr/" + fxr), ("shared/Microsoft.NETCore.App/" + runtime), ("shared/Microsoft.WindowsDesktop.App/" + desktop)]
    if sdk:
        sdk_version = newest(os.path.join(DOTNET_ROOT, "sdk"), 9)
        parts += ["sdk/" + sdk_version]
        # ワークロードの一覧 (SDK が起動の時に読む)。9.x の物を全部 (9.0.310 の SDK も 9.0.100 の名前のフォルダを読む)
        parts += ["sdk-manifests/" + d for d in os.listdir(os.path.join(DOTNET_ROOT, "sdk-manifests")) if d.startswith("9.")]
        # 参照用のパック。AspNetCore は使わないが、SDK は既定で (間接の参照に備えて) 復元の時に探す。無いと
        # NuGet から取ろうとして、インターネットに繋がらない PC では NU1100 でビルドが失敗した (確かめた時に分かった)
        for pack in ["Microsoft.NETCore.App.Ref", "Microsoft.WindowsDesktop.App.Ref", "Microsoft.AspNetCore.App.Ref", "Microsoft.NETCore.App.Host.win-x64"]:
            parts.append(f"packs/{pack}/" + newest(os.path.join(DOTNET_ROOT, "packs", pack), 9))
    files = 0
    for part in parts:
        files += copy_tree(os.path.join(DOTNET_ROOT, part), os.path.join(target, part))
    for f in ["dotnet.exe", "LICENSE.txt", "ThirdPartyNotices.txt"]:
        shutil.copy2(os.path.join(DOTNET_ROOT, f), os.path.join(target, f))
    print(f"  dotnet ({'SDK' if sdk else 'runtime'}) : {', '.join(parts)} ({files} files)")


def folder_size(path):
    return sum(os.path.getsize(os.path.join(d, f)) for d, _, fs in os.walk(path) for f in fs)


def write_text(path, text):
    # UTF-8 (BOM 付き) / CRLF (メモ帳でも読めるように)
    with open(path, "wb") as f:
        f.write(b"\xef\xbb\xbf" + text.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--out", default=os.path.join(ROOT, "Package"))
    parser.add_argument("--fresh-project", action="store_true", help="SampleProject をリポジトリから写し直す")
    args = parser.parse_args()
    name, version = engine_props()
    folder_name = f"{name.replace(' ', '')}_v{version}"
    out = os.path.abspath(args.out)
    start = time.time()

    if not args.skip_build:
        build()

    # 作り直す (v1.3 : 動かさない。以前は Luncher/Data と SampleProject を一度 _keep へ動かしていたが、
    #   Visual Studio が SampleProject を開いていると途中で止まり、Luncher/Data が _keep に残った。また Project の他の物 (ランチャーで
    #   作ったプロジェクト) を消していた)
    #   Luncher : Data (ランチャーの設定) 以外を作り直す / Editor : 全部作り直す (前の版も消す。いつも一番新しい版だけ)
    #   Project : 消さない (SampleProject が無い時か --fresh-project の時だけ写す。ある時は csproj だけ新しくする)
    os.makedirs(out, exist_ok=True)
    launcher_dir = os.path.join(out, "Luncher")
    os.makedirs(launcher_dir, exist_ok=True)
    for entry in os.listdir(launcher_dir):
        if entry.lower() == "data":
            continue
        path = os.path.join(launcher_dir, entry)
        shutil.rmtree(path) if os.path.isdir(path) else os.remove(path)
    if os.path.isdir(os.path.join(out, "Editor")):
        shutil.rmtree(os.path.join(out, "Editor"))
    os.makedirs(os.path.join(out, "Editor"))
    os.makedirs(os.path.join(out, "Project"), exist_ok=True)

    no_pdb = lambda rel: rel.lower().endswith(".pdb")

    # ---------- Luncher ----------
    launcher = os.path.join(out, "Luncher")
    n = copy_tree(os.path.join(CS, r"GameEngine.Launcher\bin\x64\Release\net9.0-windows"), launcher, no_pdb)
    print(f"Luncher : {n} files")
    copy_dotnet(os.path.join(launcher, "dotnet"), sdk=False)

    # ---------- Editor/<名前>_v<版> ----------
    editor_root = os.path.join(out, "Editor", folder_name)
    # imgui.ini (窓の配置) / ShaderCache / pipeline.cache (この PC の GPU 用のキャッシュ) は入れない
    editor_skip = lambda rel: (no_pdb(rel) or rel.lower() in ("imgui.ini", "pipeline.cache") or
                               rel.lower().startswith("shadercache" + os.sep))
    n = copy_tree(os.path.join(CS, r"GameEngine.Editor\bin\x64\Release\net9.0-windows"), os.path.join(editor_root, "Editor"), editor_skip)
    print(f"Editor : {n} files")
    shaders = os.path.join(editor_root, "Shader")
    os.makedirs(shaders)
    for shader in glob.glob(os.path.join(ROOT, r"RHI\Source\Shader\*.shader")):
        shutil.copy2(shader, shaders)
    player_skip = lambda rel: no_pdb(rel) or rel.lower() == "player.log"
    n = copy_tree(os.path.join(CS, r"GameEngine.Player\bin\Release\net9.0-windows"), os.path.join(editor_root, "Player"), player_skip)
    print(f"Player : {n} files")
    copy_dotnet(os.path.join(editor_root, "dotnet"), sdk=True)

    # ---------- Project/SampleProject ----------
    def project_skip(rel):
        low = rel.lower().replace("\\", "/")
        parts = low.split("/")
        return (low.startswith("build/") or low in ("projectversion.txt", "imgui.ini") or
                ("scripts" in parts[:1] and ("bin" in parts or "obj" in parts)) or
                any(p.startswith("_determinismtest") or p.startswith("_displaynametest") for p in parts))
    sample = os.path.join(out, "Project", "SampleProject")
    if os.path.isdir(sample) and not args.fresh_project:
        # 前の Package の SampleProject を残し、リポジトリの Scripts の csproj だけ新しくする (ビルドの仕方が版で変わるため)
        for rel in [os.path.join("Scripts", "Game.csproj"), os.path.join("Scripts", "Editor", "GameEditor.csproj")]:
            os.makedirs(os.path.dirname(os.path.join(sample, rel)), exist_ok=True)
            shutil.copy2(os.path.join(CS, "Project", rel), os.path.join(sample, rel))
        print("SampleProject : kept (csproj updated. --fresh-project to copy it again)")
    else:
        if os.path.isdir(sample):
            shutil.rmtree(sample)   # --fresh-project の時だけ
        n = copy_tree(os.path.join(CS, "Project"), sample, project_skip)
        print(f"SampleProject : {n} files")

    write_text(os.path.join(out, "README.txt"), f"""{name} {version}

Visual Studio / .NET / DirectX の SDK を入れていない PC でも動くようにまとめた物です。
(.NET と、ゲームのコードをビルドする .NET SDK は中に入っています。インストールは要りません)

フォルダ
  Luncher\\GameEngine.Launcher.exe        ランチャー。プロジェクトを選んで Open (ダブルクリックでも) するとエディターが開きます
  Editor\\{folder_name}\\Editor\\GameEngine.Editor.exe
                                          エディター (直接開くと Project\\SampleProject を開きます)
  Editor\\{folder_name}\\Player           File > Build Game で書き出すゲームの元
  Editor\\{folder_name}\\dotnet           .NET SDK (ゲームのコード Scripts\\*.cs のビルドに使う)
  Project\\SampleProject                   見本のプロジェクト

必要な物
  Windows 10 / 11 (64 bit)、DirectX 12 に対応した GPU
  (DirectX 12 / XAudio2 / D3DCompiler は Windows に入っています)

メモ
  ランチャーの設定は Luncher\\Data に書きます (フォルダごと持ち運べます)
  書き出したゲームにも .NET が入ります (Build Settings の Include .NET。約 100 MB)
""")

    print(f"\n{out}")
    for sub in ["Luncher", "Editor", "Project"]:
        print(f"  {sub:8} {folder_size(os.path.join(out, sub)) / 1024 / 1024:8.1f} MB")
    print(f"  ({time.time() - start:.0f} s)")


if __name__ == "__main__":
    main()
