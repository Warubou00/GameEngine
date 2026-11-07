# -*- coding: utf-8 -*-
# ==========================================================================
# Tsumugi Engine のアプリアイコン (TsumugiEngine.ico) を作る
#   python make_icon.py  (このフォルダに TsumugiEngine.ico と見本の TsumugiEngine_256.png を書く)
#   図柄 : 角丸の四角 (藍色) / 立方体 (3D) / 上の面に頭文字の T / 立方体を巻く金の糸 (紡ぎ)
#   Python の標準ライブラリだけで描く (PIL などは使わない)。大きさごとに 4x4 の点を取って滑らかにする
#   ICO : 16..128 は BMP (32bit + マスク)、256 は PNG (Windows の普通の形)
# ==========================================================================
import math
import os
import struct
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = [16, 24, 32, 48, 64, 128, 256]
SS = 4   # 1 画素を SS x SS の点で調べる


def lerp(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(len(a)))


def hexrgb(h):
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5))


BG_TOP, BG_BOTTOM = hexrgb("#2A2F6E"), hexrgb("#14163A")
TOP, LEFT, RIGHT = hexrgb("#9BEAF0"), hexrgb("#43B3D6"), hexrgb("#2A73B4")
LETTER = hexrgb("#1B1F4D")
THREAD = hexrgb("#F2C14E")

# 立方体 (等角投影)。座標は 0..1 (左上が 0)
CX, CY, R = 0.5, 0.53, 0.30          # 中心と、中心から角までの長さ
COS30 = math.cos(math.radians(30))
P_TOP = (CX, CY - R)
P_UL = (CX - R * COS30, CY - R * 0.5)
P_UR = (CX + R * COS30, CY - R * 0.5)
P_C = (CX, CY)
P_LL = (CX - R * COS30, CY + R * 0.5)
P_LR = (CX + R * COS30, CY + R * 0.5)
P_BOT = (CX, CY + R)
FACE_TOP = [P_TOP, P_UR, P_C, P_UL]
FACE_LEFT = [P_UL, P_C, P_BOT, P_LL]
FACE_RIGHT = [P_C, P_UR, P_LR, P_BOT]


def top_face_point(u, v):
    # 上の面の (u, v) (0..1) を画面の位置に。u : 左上の辺の向き、v : 右上の辺の向き
    ox, oy = P_UL
    ax, ay = P_TOP[0] - P_UL[0], P_TOP[1] - P_UL[1]   # u の向き (左の角 → 上の角)
    bx, by = P_C[0] - P_UL[0], P_C[1] - P_UL[1]       # v の向き (左の角 → 手前の角)
    return (ox + ax * u + bx * v, oy + ay * u + by * v)


def quad(u0, v0, u1, v1):
    return [top_face_point(u0, v0), top_face_point(u1, v0), top_face_point(u1, v1), top_face_point(u0, v1)]


# 頭文字 T (上の面に彫った形。横棒と縦棒)
LETTER_T = [quad(0.20, 0.20, 0.80, 0.36), quad(0.42, 0.36, 0.58, 0.82)]


def inside(poly, x, y):
    # 凸な多角形 (時計回りでも反時計回りでもよい) の中か
    sign = 0
    n = len(poly)
    for i in range(n):
        x0, y0 = poly[i]
        x1, y1 = poly[(i + 1) % n]
        c = (x1 - x0) * (y - y0) - (y1 - y0) * (x - x0)
        if c != 0:
            s = 1 if c > 0 else -1
            if sign == 0:
                sign = s
            elif s != sign:
                return False
    return True


def rounded_rect(x, y, margin=0.04, radius=0.22):
    lo, hi = margin, 1 - margin
    if x < lo or x > hi or y < lo or y > hi:
        return False
    cx = min(max(x, lo + radius), hi - radius)
    cy = min(max(y, lo + radius), hi - radius)
    return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2


# 金の糸 : 立方体を斜めに巻く楕円の輪 (奥の半分は立方体の後ろ、手前の半分は前)
THREAD_A, THREAD_B, THREAD_ANGLE, THREAD_W = 0.43, 0.13, math.radians(-18), 0.022


def thread(x, y):
    # 戻り値 : None (糸ではない) / "back" / "front"
    dx, dy = x - CX, y - (CY + 0.02)
    ca, sa = math.cos(-THREAD_ANGLE), math.sin(-THREAD_ANGLE)
    rx, ry = dx * ca - dy * sa, dx * sa + dy * ca
    k = math.sqrt((rx / THREAD_A) ** 2 + (ry / THREAD_B) ** 2)
    if k == 0:
        return None
    # 楕円からの距離 (近似 : 半径の比 × 平均の半径)
    dist = abs(k - 1) * math.sqrt(rx * rx + ry * ry) / k
    if dist > THREAD_W:
        return None
    return "back" if ry < 0 else "front"


def sample(x, y):
    # 1 点の色 (r, g, b, a)。下から順に重ねる
    if not rounded_rect(x, y):
        return (0, 0, 0, 0)
    color = lerp(BG_TOP, BG_BOTTOM, y)
    t = thread(x, y)
    if t == "back":
        color = lerp(color, THREAD, 0.55)   # 奥の糸は少し暗く (背景に沈める)
    cube = None
    if inside(FACE_TOP, x, y):
        cube = TOP
        for part in LETTER_T:
            if inside(part, x, y):
                cube = LETTER
    elif inside(FACE_LEFT, x, y):
        cube = LEFT
    elif inside(FACE_RIGHT, x, y):
        cube = RIGHT
    if cube is not None:
        color = cube
    if t == "front":
        color = THREAD
    return color + (1.0,)


def render(size):
    pixels = []
    for py in range(size):
        row = []
        for px in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(SS):
                for sx in range(SS):
                    r, g, b, a = sample((px + (sx + 0.5) / SS) / size, (py + (sy + 0.5) / SS) / size)
                    acc[0] += r * a
                    acc[1] += g * a
                    acc[2] += b * a
                    acc[3] += a
            n = SS * SS
            a = acc[3] / n
            if a > 0:
                rgb = [min(255, int(round(c / acc[3] * 255))) for c in acc[:3]]
            else:
                rgb = [0, 0, 0]
            row.append((rgb[0], rgb[1], rgb[2], int(round(a * 255))))
        pixels.append(row)
    return pixels


def png(pixels):
    size = len(pixels)
    raw = b"".join(b"\x00" + bytes(c for p in row for c in p) for row in pixels)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def bmp(pixels):
    # ICO の中の BMP : BITMAPINFOHEADER (高さは 2 倍 = 色 + マスク) / 下の行から BGRA / 1bit の AND マスク (透明は 1)
    size = len(pixels)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    color = b"".join(bytes((p[2], p[1], p[0], p[3])) for row in reversed(pixels) for p in row)
    stride = ((size + 31) // 32) * 4
    mask = bytearray()
    for row in reversed(pixels):
        bits = bytearray(stride)
        for x, p in enumerate(row):
            if p[3] == 0:
                bits[x // 8] |= 0x80 >> (x % 8)
        mask += bits
    return header + color + bytes(mask)


def main():
    images = []
    for size in SIZES:
        pixels = render(size)
        images.append((size, png(pixels) if size >= 256 else bmp(pixels)))
        if size == 256:
            with open(os.path.join(HERE, "TsumugiEngine_256.png"), "wb") as f:
                f.write(png(pixels))
        print(f"{size}x{size}")
    data = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, blobs = b"", b""
    for size, blob in images:
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
        blobs += blob
    with open(os.path.join(HERE, "TsumugiEngine.ico"), "wb") as f:
        f.write(data + entries + blobs)
    print("TsumugiEngine.ico")


if __name__ == "__main__":
    main()
