# -*- coding: utf-8 -*-
# ==========================================================================
# エンジンのリファレンス (Docs/Reference/index.html) を作る
#   python Docs/Reference/make_reference.py
#   ソース (C# / C++) を読んで、型 (クラス / 構造体 / インターフェイス / 列挙)・継承・メンバー・コメント・
#   DLL の公開関数と C# の宣言の対応を集め、template.html に JSON として埋め込む (1 つのファイルで開ける)
#   ソースを変えたら、これを動かし直せば新しくなる。手で書いた説明 (概要 / 図 / 手順) は template.html にある
#
#   読み方は簡単な物 (コンパイラーではない) :
#     - 文字列とコメントを消した行で { } を数えて、型の中のメンバーを見つける
#     - private のメンバーは出さない (public / internal / protected だけ)
#     - コメントは宣言のすぐ上 (/// / // / /** */) と、同じ行の後ろの // を説明にする
# ==========================================================================
import datetime
import html
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

# ---------- モジュール (プロジェクト) ----------
# id, 名前, 言語, 種類, ソースのフォルダ, 説明, 使う物
MODULES = [
    ("rhi",      "RHI",               "C++", "DLL (RHI.dll)",        ["RHI/Source"],       "DirectX 12 のレンダリング ハードウェア インターフェイス。デバイス / コマンド / パイプライン / テクスチャ / GPU カリング / ExecuteIndirect / シェーダーのコンパイル (DXC)。公開するのは抽象クラス (Public/rhi.h) と作る関数だけ", ["core"]),
    ("renderer", "Renderer",          "C++", "DLL (Renderer.dll)",   ["Renderer/Source"],  "C# から呼ぶ描画の入口 (extern \"C\" の関数)。モデルの読み込み (OBJ / PMX)、描画オブジェクト、マテリアル、カメラを持ち、RHI に描かせる", ["rhi", "core"]),
    ("physics",  "Physics",           "C++", "DLL (Physics.dll)",    ["Physics/Source"],   "当たり判定と剛体 (球 / 箱 / カプセル、接触の解決、ジョイント、レイキャスト)", []),
    ("audio",    "Audio",             "C++", "DLL (Audio.dll)",      ["Audio/Source"],     "XAudio2 の音 (WAV の読み込み、3D の音)", ["core"]),
    ("core",     "Core",              "C++", "DLL (Core.dll)",       ["Core/Source"],      "共通 (ログ / ファイル / 数学 / ハッシュ) とゲームパッド (XInput / DirectInput)", []),
    ("runtime",  "GameEngine.Runtime", "C#", "DLL (GameEngine.dll)", ["CSharp_console/GameEngine.Runtime"], "エンジンの本体 (C#)。ECS (World / Entity / コンポーネント / システム)、描画パイプライン、アセット、シーンの保存、入力、時間。C++ の DLL を P/Invoke で呼ぶ", ["renderer", "physics", "audio", "core"]),
    ("editor",   "GameEngine.Editor", "C#", "exe",                  ["CSharp_console/GameEngine.Editor"],  "エディター (ImGui)。Hierarchy / Inspector / Project / シーンビュー / 元に戻す / プレハブ / ゲームの書き出し / スクリプトの読み直し / シェーダーグラフ / 自動テスト", ["runtime"]),
    ("player",   "GameEngine.Player", "C#", "exe",                  ["CSharp_console/GameEngine.Player"],  "書き出したゲームの起動 (エディター無しでシーンを動かす)", ["runtime"]),
    ("launcher", "GameEngine.Launcher", "C#", "exe",                ["CSharp_console/GameEngine.Launcher"], "ランチャー (プロジェクトとエディターの版を選んで起動)", []),
    ("console",  "GameEngine_console", "C#", "exe (見本)",          ["CSharp_console/GameEngine_console"], "見本のアプリ (エンジンを直接使う例 / 性能の確認 / テスト)", ["runtime"]),
    ("scripts",  "Project/Scripts",   "C#", "ゲームのコード (Game.dll)", ["CSharp_console/Project/Scripts"], "プロジェクトのスクリプト (ユーザーのコンポーネント / システム / エディターの拡張の例)。エディターが実行中にコンパイルして読み直す", ["runtime"]),
]
SKIP_DIRS = {"bin", "obj", "x64", "Debug", "Release", ".vs", "DirectXTex", "ImGui", "Library", "Build"}

# ---------- 読む ----------
def read(path):
    data = open(path, "rb").read()
    for enc in ("utf-8-sig", "cp932"):
        try:
            return data.decode(enc).replace("\r\n", "\n").replace("\r", "\n")
        except UnicodeDecodeError:
            pass
    return data.decode("utf-8", errors="replace")

def clean_code(text, lang):
    """コメントと文字列の中身を消す (行の数は同じ)。戻り値 = 行のリスト"""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append("".join("\n" for ch in text[i:j] if ch == "\n") or " ")
            i = j
            continue
        if lang == "C#" and text.startswith('"""', i):
            q = 3
            while i + q < n and text[i + q] == '"':
                q += 1
            end = text.find('"' * q, i + q)
            end = n if end < 0 else end + q
            out.append('""' + "".join("\n" for ch in text[i:end] if ch == "\n"))
            i = end
            continue
        if c == '"' or (c in "@$" and i + 1 < n and text[i + 1] in '"@$'):
            verbatim = False
            j = i
            while j < n and text[j] in "@$":
                verbatim = verbatim or text[j] == "@"
                j += 1
            if j >= n or text[j] != '"':
                out.append(c)
                i += 1
                continue
            j += 1
            while j < n:
                if verbatim and text[j] == '"' and j + 1 < n and text[j + 1] == '"':
                    j += 2
                    continue
                if not verbatim and text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"' or (not verbatim and text[j] == "\n"):
                    break
                j += 1
            seg = text[i:j + 1]
            out.append('""' + "".join("\n" for ch in seg if ch == "\n"))
            i = j + 1
            continue
        if c == "'":
            m = re.match(r"'(\\.[^']*|[^'\\\n])'", text[i:i + 12])
            if m:
                out.append("' '")
                i += len(m.group(0))
                continue
        out.append(c)
        i += 1
    return "".join(out).split("\n")

def strip_doc(lines):
    """コメントの行 → 説明の文"""
    result = []
    for line in lines:
        s = line.strip()
        s = re.sub(r"^(///|//|/\*+|\*+/|\*)", "", s).strip()
        s = re.sub(r"\*+/$", "", s).strip()
        if re.fullmatch(r"[=\-*#~_ ]{3,}", s) or s == "":
            if result and result[-1] != "":
                result.append("")
            continue
        s = re.sub(r"</?(summary|remarks|returns|para|c|code)>", "", s)
        s = re.sub(r'<(?:see|paramref|typeparamref)\s+(?:cref|name)="([^"]+)"\s*/>', r"\1", s)
        s = re.sub(r'<param name="(\w+)">', r"\1 : ", s).replace("</param>", "")
        s = re.sub(r"^@(brief|details)\s*", "", s)
        s = html.unescape(s)   # /// の中の &lt;T&gt; → <T>
        if s.startswith(("@file", "@author", "@date", "@version", "@copyright", "Copyright")):
            continue
        result.append(s)
    while result and result[-1] == "":
        result.pop()
    while result and result[0] == "":
        result.pop(0)
    text = "\n".join(result)
    return text[:2500] + (" ..." if len(text) > 2500 else "")

def comment_above(raw, index):
    """宣言 (index 行目、0 から) のすぐ上のコメント。属性 / テンプレートの行は飛ばす"""
    j = index - 1
    while j >= 0 and re.match(r"^\s*(\[.*\]\s*)$|^\s*template\s*<", raw[j]):
        j -= 1
    block = []
    in_block = False
    while j >= 0:
        s = raw[j].strip()
        if in_block:
            block.insert(0, raw[j])
            if s.startswith("/*"):
                in_block = False
            j -= 1
            continue
        if s.startswith("//"):
            block.insert(0, raw[j])
            j -= 1
            continue
        if s.endswith("*/"):
            block.insert(0, raw[j])
            if not s.startswith("/*"):
                in_block = True
            j -= 1
            continue
        break
    return strip_doc(block)

def trailing_comment(raw_line):
    m = re.search(r'//(.*)$', re.sub(r'"(?:\\.|[^"\\])*"', '""', raw_line))
    return m.group(1).strip().lstrip("/").strip() if m else ""

def squash(s, limit=220):
    s = re.sub(r"\s+", " ", s).strip()
    return s if len(s) <= limit else s[:limit] + " …"

# ---------- C# ----------
CS_TYPE = re.compile(r"^\s*((?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|unsafe|ref|file|new)\s+)*)"
                     r"(record\s+struct|record\s+class|record|class|struct|interface|enum)\s+([A-Za-z_]\w*)\s*(<[^>]*>)?")
CS_MODS = {"public", "internal", "protected", "private", "static", "readonly", "const", "virtual", "override", "abstract", "sealed",
           "new", "extern", "unsafe", "async", "partial", "required", "volatile", "event", "implicit", "explicit", "ref", "fixed"}

def balance_join(clean, i, opener="(", closer=")"):
    """i 行目から ( ) が釣り合うまで繋ぐ。戻り値 = (文, 最後の行)"""
    text = clean[i]
    j = i
    while text.count(opener) > text.count(closer) and j + 1 < len(clean) and j - i < 40:
        j += 1
        text += " " + clean[j].strip()
    return text, j

def parse_cs(path, module, types):
    raw = read(path).split("\n")
    clean = clean_code("\n".join(raw), "C#")
    rel = os.path.relpath(path, ROOT).replace("\\", "/")
    namespace = ""
    depth = 0
    stack = []          # (type, 中の深さ)
    pending = None      # 宣言したが { がまだの型
    skip_until = -1
    for i, line in enumerate(clean):
        s = line.strip()
        m = re.match(r"^namespace\s+([\w.]+)\s*;", s)
        if m:
            namespace = m.group(1)
        m2 = re.match(r"^namespace\s+([\w.]+)\s*\{?$", s)
        if m2:
            namespace = m2.group(1)
        if i > skip_until and s and not s.startswith("#"):
            inside = stack[-1] if stack and depth == stack[-1][1] else None
            tm = CS_TYPE.match(line)
            if tm and (inside is not None or not stack or depth == stack[-1][1]) and not re.match(r"^\s*(?:return|new|var)\b", line):
                mods = tm.group(1).split()
                kind = tm.group(2).replace("  ", " ")
                name = tm.group(3)
                decl, last = balance_join(clean, i)
                # 継承 : 名前 (と主コンストラクター) の後の :
                after = decl[tm.end():]
                if after.lstrip().startswith("("):
                    k, d = 0, 0
                    for k, ch in enumerate(after):
                        d += ch == "("
                        d -= ch == ")"
                        if d == 0 and ch == ")":
                            break
                    params = after[after.index("(") + 1:k]
                    after = after[k + 1:]
                else:
                    params = None
                bases = []
                j2 = last
                if "{" not in after and ";" not in after and j2 + 1 < len(clean) and clean[j2 + 1].strip().startswith(":"):
                    j2 += 1
                    after += " " + clean[j2].strip()
                bm = re.match(r"^\s*:\s*([^{;]+)", after)
                if bm:
                    bases = [b.strip() for b in re.split(r",(?![^<]*>)", re.sub(r"\bwhere\b.*$", "", bm.group(1))) if b.strip()]
                    bases = [re.sub(r"\(.*$", "", b).strip() for b in bases]
                access = next((a for a in ("public", "internal", "protected", "private") if a in mods), "internal" if not stack else "private")
                parent = stack[-1][0] if stack else None
                full = (namespace + "." if namespace else "") + (parent["name"] + "." if parent else "") + name
                if tm.group(4):   # ジェネリック (ComponentEditor<T> と ComponentEditor は別の型)
                    full += "<" + "," * tm.group(4).count(",") + ">"
                t = {
                    "id": full, "name": name, "kind": kind.replace("record class", "record"), "lang": "C#", "module": module,
                    "ns": namespace, "file": rel, "line": i + 1, "bases": bases, "access": access,
                    "mods": [x for x in mods if x in ("static", "sealed", "abstract", "partial", "readonly", "unsafe")],
                    "doc": comment_above(raw, i), "members": [], "parent": parent["id"] if parent else None,
                    "generic": (tm.group(4) or "").strip(),
                }
                if params:
                    for p in [x.strip() for x in re.split(r",(?![^<(]*[>)])", params) if x.strip()]:
                        pm = re.match(r"^(?:\[[^\]]*\]\s*)?(.+?)\s+([A-Za-z_]\w*)(\s*=.*)?$", p)
                        if pm:
                            t["members"].append({"kind": "property", "name": pm.group(2), "sig": squash(f"{pm.group(1)} {pm.group(2)}{pm.group(3) or ''}"), "doc": "", "line": i + 1, "access": "public", "static": False})
                # 同じファイルの partial / 別のファイルの partial は 1 つにまとめる (後で)
                types.append(t)
                rest = decl[tm.end():]
                if re.search(r"\{", "".join(clean[i:j2 + 1])) or j2 + 1 < len(clean) and clean[j2 + 1].strip().startswith("{"):
                    pending = (t, depth + 1)
                elif rest.rstrip().endswith(";"):
                    pending = None
                else:
                    pending = (t, depth + 1)
                skip_until = j2 if "{" not in "".join(clean[i:j2 + 1]) else j2
            elif inside is not None:
                parse_cs_member(inside[0], raw, clean, i)
        # 深さ
        for ch in line:
            if ch == "{":
                depth += 1
                if pending and depth == pending[1]:
                    stack.append((pending[0], depth))
                    pending = None
            elif ch == "}":
                if stack and depth == stack[-1][1]:
                    stack.pop()
                depth -= 1

CS_SKIP = re.compile(r"^\s*(\[|#|using\b|return\b|if\b|else\b|for\b|foreach\b|while\b|switch\b|case\b|default\s*:|get\b|set\b|init\b|add\b|remove\b|\}|\{|=>|//|base\s*\(|this\s*\()")

def parse_cs_member(t, raw, clean, i):
    line = clean[i]
    s = line.strip()
    if not s or CS_SKIP.match(s):
        # 属性だけの行の後ろに宣言がある時 (例 : [Range(0, 1)] public float X;)
        m = re.match(r"^\s*(?:\[[^\]]*\]\s*)+(.+)$", line)
        if not m:
            return
        s = m.group(1).strip()
    if t["kind"] == "enum":
        for part in s.split(","):
            pm = re.match(r"^\s*([A-Za-z_]\w*)\s*(=\s*[^,]+)?\s*$", part)
            if pm:
                t["members"].append({"kind": "value", "name": pm.group(1), "sig": squash(part.strip()), "doc": trailing_comment(raw[i]) or comment_above(raw, i),
                                     "line": i + 1, "access": "public", "static": True})
        return
    text, last = balance_join(clean, i) if "(" in s else (s, i)
    text = re.sub(r"^\s*(?:\[[^\]]*\]\s*)+", "", text).strip()
    words = text.split()
    mods = []
    while words and words[0] in CS_MODS:
        mods.append(words.pop(0))
    if not words:
        return
    access = next((a for a in ("public", "internal", "protected", "private") if a in mods), "public" if t["kind"] == "interface" else "private")
    if access == "private":
        return
    rest = " ".join(words)
    static = "static" in mods or "const" in mods
    head = re.split(r"\s*(?:\{|=>|;)", rest)[0]
    # 型の中の ( ) (タプル : (int A, int B) X / List<(string, int)> X) は関数の ( ではない
    masked = head
    while True:
        tm = re.search(r"(^|<|,\s*)\(([^()]*)\)", masked)
        if not tm:
            break
        masked = masked[:tm.start(2) - 1] + "T" * (tm.end(2) - tm.start(2) + 2) + masked[tm.end(2) + 1:]
    paren = masked.find("(")
    if paren >= 0 and (("=" not in masked) or paren < masked.index("=")):
        before = head[:paren].strip()
        name = before.split()[-1] if before.split() else before
        if name.startswith("operator") or " operator" in before:
            kind, name = "operator", before.split("operator")[-1].strip() or "operator"
            name = "operator " + name
        elif before == t["name"] or before.endswith(" " + t["name"]) and len(before.split()) == 1:
            kind = "constructor"
        elif before.split()[0] == "delegate" if before.split() else False:
            kind = "delegate"
        else:
            kind = "method"
        name = re.sub(r"<.*$", "", name)
        k = head.rfind(")")
        sig = " ".join(m for m in mods if m not in ("public", "internal", "protected")) + " " + head[:k + 1]
    elif "this[" in head:
        kind, name, sig = "indexer", "this[]", " ".join(mods) + " " + head
    else:
        cut = re.search(r"\s*=(?!>)", masked)
        decl = head[:cut.start()] if cut else head
        parts = masked[:cut.start() if cut else len(masked)].split()
        if len(parts) < 2 and not ("event" in mods):
            return
        name = re.sub(r"\[.*$", "", parts[-1].rstrip(",;"))   # fixed uint Colors[4] → Colors
        if not re.match(r"^[A-Za-z_]\w*$", name):
            return
        is_prop = bool(re.search(r"^\s*[^;=]*\b" + re.escape(name) + r"\s*(\{|=>)", rest)) or (last + 1 < len(clean) and clean[last + 1].strip().startswith("{") and not s.endswith(";"))
        kind = "event" if "event" in mods else ("property" if is_prop else ("constant" if "const" in mods else "field"))
        sig = " ".join(m for m in mods if m not in ("public", "internal", "protected")) + " " + decl
        if is_prop:
            acc = re.search(r"\{\s*((?:(?:private|internal|protected|public)?\s*(?:get|set|init)\s*[;{]?\s*)+)", rest)
            if acc:
                sig += " { " + " ".join(re.findall(r"(?:(?:private|internal|protected)\s+)?(?:get|set|init)", acc.group(1))) + " }"
            elif "=>" in rest:
                sig += " { get }"
    doc = comment_above(raw, i)
    tail = trailing_comment(raw[i])
    if tail and tail not in doc:
        doc = (doc + "\n" + tail).strip()
    t["members"].append({"kind": kind, "name": name, "sig": squash(sig), "doc": doc, "line": i + 1, "access": access, "static": static})

# ---------- C++ ----------
CPP_TYPE = re.compile(r"^\s*(?:template\s*<[^>]*>\s*)?(class|struct|enum\s+class|enum\s+struct|enum|union)\s+(?:alignas\s*\([^)]*\)\s*)?(?:[A-Z][A-Z0-9_]*_API(?:_CLASS)?\s+|__declspec\([^)]*\)\s+)?([A-Za-z_]\w*)\s*(final)?\s*(?::\s*([^{;]+?))?\s*(\{.*)?$")

def parse_cpp(path, module, types, exports):
    raw = read(path).split("\n")
    clean = clean_code("\n".join(raw), "C++")
    rel = os.path.relpath(path, ROOT).replace("\\", "/")
    ns = []
    depth = 0
    stack = []           # [type, 深さ, access]
    pending = None
    pending_ns = None
    ns_depths = []
    skip_until = -1
    for i, line in enumerate(clean):
        s = line.strip()
        if i > skip_until and s and not s.startswith("#"):
            inside = stack[-1] if stack and depth == stack[-1][1] else None
            nm = re.match(r"^namespace\s+([\w:]+)\s*(\{)?", s)
            tm = CPP_TYPE.match(line)
            # 公開関数 (XXX_API 戻り値 名前(...))
            em = re.match(r"^\s*([A-Z][A-Z0-9]*_API)\s+(.+?)\s+\**([A-Za-z_]\w*)\s*\(", line)
            if em and not stack and path.lower().endswith(".cpp"):
                decl, last = balance_join(clean, i)
                head = decl[:decl.rfind(")") + 1] if ")" in decl else decl
                exports.append({"module": module, "name": em.group(3), "sig": squash(head.replace(em.group(1), "").strip(), 400),
                                "doc": comment_above(raw, i), "file": rel, "line": i + 1})
            if nm:
                pending_ns = nm.group(1)
            elif tm and not s.endswith(";") and not re.match(r"^\s*(friend|typedef)\b", line) and (not stack or depth == stack[-1][1]):
                kind = tm.group(1).replace("  ", " ")
                name = tm.group(2)
                bases_text = tm.group(4) or ""
                j2 = i
                if not tm.group(5) and j2 + 1 < len(clean) and clean[j2 + 1].strip().startswith(":"):
                    j2 += 1
                    bases_text = clean[j2].strip()[1:].split("{")[0]
                bases = [re.sub(r"^(public|protected|private|virtual)\s+", "", b.strip()) for b in bases_text.split(",") if b.strip()]
                bases = [re.sub(r"^(public|protected|private|virtual)\s+", "", b) for b in bases]
                if kind.startswith("enum") and bases:
                    bases = []   # enum class X : uint8_t は継承ではない
                parent = stack[-1][0] if stack else None
                full = "::".join(ns + ([parent["name"]] if parent else []) + [name])
                t = {"id": full, "name": name, "kind": "enum" if kind.startswith("enum") else kind, "lang": "C++", "module": module,
                     "ns": "::".join(ns), "file": rel, "line": i + 1, "bases": bases, "access": "public", "mods": ["final"] if tm.group(3) else [],
                     "doc": comment_above(raw, i), "members": [], "parent": parent["id"] if parent else None, "generic": ""}
                types.append(t)
                pending = (t, depth + 1, "private" if kind == "class" else "public")
                skip_until = j2
            elif inside is not None:
                am = re.match(r"^(public|protected|private)\s*:", s)
                if am:
                    inside[2] = am.group(1)
                elif inside[2] in ("public", "protected"):
                    parse_cpp_member(inside[0], raw, clean, i, inside[2])
        for ch in line:
            if ch == "{":
                depth += 1
                if pending and depth == pending[1]:
                    stack.append([pending[0], depth, pending[2]])
                    pending = None
                elif pending_ns is not None:
                    ns.extend(pending_ns.split("::"))
                    ns_depths.append((depth, len(pending_ns.split("::"))))
                    pending_ns = None
            elif ch == "}":
                if stack and depth == stack[-1][1]:
                    stack.pop()
                if ns_depths and ns_depths[-1][0] == depth:
                    _, count = ns_depths.pop()
                    del ns[-count:]
                depth -= 1

CPP_SKIP = re.compile(r"^\s*(friend|using|typedef|static_assert|return|if|for|while|switch|case|default|break|#|\}|\{|[A-Z_]+\s*\(|template)")

def parse_cpp_member(t, raw, clean, i, access):
    s = clean[i].strip()
    if not s or CPP_SKIP.match(s):
        return
    if t["kind"] == "enum":
        for part in s.split(","):
            pm = re.match(r"^\s*([A-Za-z_]\w*)\s*(=\s*[^,]+)?\s*$", part)
            if pm:
                t["members"].append({"kind": "value", "name": pm.group(1), "sig": squash(part.strip()), "doc": trailing_comment(raw[i]),
                                     "line": i + 1, "access": "public", "static": True})
        return
    text, last = balance_join(clean, i)
    head = re.split(r"\s*(?:\{|;)", text)[0].strip()
    if not head or head.endswith(":"):
        return
    static = bool(re.match(r"^(static|constexpr\s+static|static\s+constexpr)\b", head))
    if "(" in head and not re.match(r"^[^(]*\[", head):   # 配列のフィールド (float a[4]) 以外で ( があれば関数
        before = head[:head.index("(")].strip()
        name = before.split()[-1] if before.split() else before
        name = name.lstrip("*&")
        if "operator" in before:
            kind, name = "operator", "operator" + before.split("operator")[-1]
        elif name.lstrip("~") == t["name"]:
            kind = "constructor" if not name.startswith("~") else "destructor"
        else:
            kind = "method"
        k = head.rfind(")")
        tail = head[k + 1:].strip()
        sig = head[:k + 1] + (" " + tail if tail else "")
        sig = re.sub(r"\s*=\s*0\s*$", " = 0", sig)
    else:
        decl = re.split(r"\s*=|\s*\{", head)[0]
        parts = decl.split()
        if len(parts) < 2:
            return
        name = re.sub(r"\[.*$", "", parts[-1]).lstrip("*&")
        if not re.match(r"^[A-Za-z_]\w*$", name):
            return
        kind, sig = "field", decl
    doc = comment_above(raw, i)
    tail = trailing_comment(raw[i])
    if tail and tail not in doc:
        doc = (doc + "\n" + tail).strip()
    t["members"].append({"kind": kind, "name": name, "sig": squash(sig), "doc": doc, "line": i + 1, "access": access, "static": static})

# ---------- 全部 ----------
def walk(folder, exts):
    for base, dirs, files in os.walk(folder):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".")]
        for f in sorted(files):
            if os.path.splitext(f)[1].lower() in exts:
                yield os.path.join(base, f)

def merge_partials(types):
    merged = {}
    order = []
    for t in types:
        key = (t["lang"], t["module"], t["id"])
        if key in merged:
            m = merged[key]
            m.setdefault("parts", [m["file"] + ":" + str(m["line"])]).append(t["file"] + ":" + str(t["line"]))
            m["members"].extend(t["members"])
            m["bases"] = m["bases"] or t["bases"]
            if len(t["doc"]) > len(m["doc"]):
                m["doc"] = t["doc"]
        else:
            merged[key] = t
            order.append(key)
    return [merged[k] for k in order]

def link_uses(types):
    """メンバーの型に出てくる、同じ言語の型の名前 → 使う (持つ) 関係"""
    by_name = {}
    for t in types:
        by_name.setdefault((t["lang"], t["name"]), []).append(t)
    for t in types:
        uses = set()
        for m in t["members"]:
            if m["kind"] not in ("field", "property", "constant", "event"):
                continue
            type_part = m["sig"].rsplit(" " + m["name"], 1)[0]
            for word in set(re.findall(r"[A-Za-z_]\w*", type_part)):
                for other in by_name.get((t["lang"], word), []):
                    if other is not t and other["kind"] != "enum":
                        uses.add(other["id"])
        t["uses"] = sorted(uses)

def main():
    types, exports, files = [], [], {}
    for mid, name, lang, kind, folders, desc, deps in MODULES:
        count = 0
        for folder in folders:
            full = os.path.join(ROOT, folder)
            if not os.path.isdir(full):
                continue
            if lang == "C#":
                for path in walk(full, {".cs"}):
                    parse_cs(path, mid, types)
                    count += 1
            else:
                for path in walk(full, {".h", ".hpp", ".cpp"}):
                    parse_cpp(path, mid, types, exports)
                    count += 1
        files[mid] = count
    types = merge_partials(types)
    # 同じ id (別のモジュールの同じ名前) は区別する
    seen = {}
    for t in types:
        key = t["id"]
        if key in seen:
            t["id"] = f"{t['id']}@{t['module']}"
        seen[t["id"]] = True
    # 説明が無い型 : ファイルの先頭の説明 (1 つのファイルに型がいくつもあり、説明が最初の型の上にある時) を使う
    file_docs = {}
    for t in types:
        file_docs.setdefault(t["file"], t["doc"] if t["doc"] else None)
    for f in list(file_docs):
        if not file_docs[f]:
            raw = read(os.path.join(ROOT, f)).split(chr(10))
            head = []
            for line in raw:   # ファイルの先頭のコメント (/** @file ... @brief ... */ や // の見出し)
                s = line.strip()
                if s.startswith(("//", "/*", "*")) or (head and not s):
                    head.append(line)
                elif (not s or s.startswith(("#pragma", "#ifndef", "#define", "using", "namespace"))) and not head:
                    continue   # 先頭の空行 / using などは飛ばす (以前は先頭の空行で止まり、rhi.h の説明が出なかった)
                else:
                    break
            file_docs[f] = strip_doc(head) or None
    for t in types:
        if not t["doc"] and file_docs.get(t["file"]):
            t["doc"] = file_docs[t["file"]]
            t["docFromFile"] = True
    link_uses(types)
    # C# の P/Invoke の宣言と C++ の公開関数を繋ぐ
    pinvoke = {}
    for path in walk(os.path.join(ROOT, "CSharp_console", "GameEngine.Runtime", "Native"), {".cs"}):
        raw = read(path).split("\n")
        for i, line in enumerate(raw):
            m = re.match(r"^\s*(?:public|internal)\s+static\s+extern\s+(.+?)\s+([A-Za-z_]\w*)\s*\((.*)", line)
            if m:
                text = line
                j = i
                while text.count("(") > text.count(")") and j + 1 < len(raw):
                    j += 1
                    text += " " + raw[j].strip()
                sig = re.sub(r"^\s*(public|internal)\s+static\s+extern\s+", "", text).rstrip(";").strip()
                pinvoke[m.group(2)] = {"sig": squash(sig, 400), "file": os.path.relpath(path, ROOT).replace("\\", "/"), "line": i + 1}
    for e in exports:
        e["cs"] = pinvoke.get(e["name"])
    # 同じ並びの構造体 : 公開関数の同じ位置の引数の型 (C# の宣言 ↔ C++)。名前が違っても分かる
    # (以前は名前だけで組にしていて、C# の MaterialDesc を RHI::MaterialDesc と比べていた。本当は RendererMaterialDesc)
    def param_types(sig):
        inner = sig[sig.find("(") + 1:sig.rfind(")")]
        result = []
        depth, cur = 0, ""
        for ch in inner + ",":
            depth += ch in "<(["
            depth -= ch in ">)]"
            if ch == "," and depth == 0:
                p = re.sub(r"\[[^\]]*\]", "", cur)
                words = re.findall(r"[A-Za-z_][\w:]*", p)
                words = [w for w in words if w not in ("const", "struct", "ref", "out", "in", "unsigned", "params", "scoped")]
                result.append(words[-2].split("::")[-1] if len(words) >= 2 else "")
                cur = ""
            else:
                cur += ch
        return result
    native_cs = {t["name"]: t for t in types if t["lang"] == "C#" and t["ns"] == "GameEngine.Native" and t["kind"] == "struct"}
    pairs = {}
    for e in exports:
        if not e["cs"]:
            continue
        for a, b in zip(param_types(e["cs"]["sig"]), param_types(e["sig"])):
            if a in native_cs and a not in pairs:
                cands = [t for t in types if t["lang"] == "C++" and t["kind"] == "struct" and t["name"] == b]
                cands.sort(key=lambda t: t["module"] != e["module"])
                if cands:
                    pairs[a] = {"cs": native_cs[a]["id"], "cpp": cands[0]["id"], "via": e["name"]}
    try:
        commit = subprocess.run(["git", "-C", ROOT, "log", "-1", "--format=%h %ad %s", "--date=short"], capture_output=True, text=True, encoding="utf-8").stdout.strip()
        branch = subprocess.run(["git", "-C", ROOT, "rev-parse", "--abbrev-ref", "HEAD"], capture_output=True, text=True).stdout.strip()
    except OSError:
        commit, branch = "", ""
    data = {
        "generated": datetime.datetime.now().strftime("%Y-%m-%d %H:%M"),
        "commit": commit, "branch": branch, "root": ROOT.replace("\\", "/"),
        "modules": [{"id": m[0], "name": m[1], "lang": m[2], "kind": m[3], "path": m[4][0], "desc": m[5], "deps": m[6], "files": files.get(m[0], 0)} for m in MODULES],
        "types": types,
        "exports": exports,
        "structPairs": list(pairs.values()),
    }
    template = read(os.path.join(HERE, "template.html"))
    payload = json.dumps(data, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
    html = template.replace("/*__DATA__*/null", payload)
    out = os.path.join(HERE, "index.html")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    members = sum(len(t["members"]) for t in types)
    print(f"{out} : {len(types)} types, {members} members, {len(exports)} exports ({sum(1 for e in exports if e['cs'])} with C# declarations), {len(html) // 1024} KB")
    for mid in files:
        print(f"  {mid:9s} {files[mid]:4d} files {sum(1 for t in types if t['module'] == mid):4d} types")

if __name__ == "__main__":
    main()
