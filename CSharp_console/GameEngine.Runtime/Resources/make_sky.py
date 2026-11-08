# -*- coding: utf-8 -*-
# ==========================================================================
# 既定の空 (DefaultSky.png) を作る (v1.4)。エンジンの Skybox が何も設定されていない時に使う
#   python make_sky.py  (このフォルダに DefaultSky.png を書く。GameEngine.Runtime に埋め込まれる)
#   形 : 正距円筒 (横 = 方位 360 度 / 縦 = 上 90 度 〜 下 -90 度)。2048 x 1024
#   中身 : 天頂の青 → 地平の明るい水色 / 太陽 (方位 40 度・高さ 35 度) と光の輪 / 雲 (値のノイズを重ねた物) / 地平より下は霞んだ地面
#   Python の標準ライブラリだけで描く (numpy なども使わない)。行ごとに分けて、CPU の数だけ並べて描く
# ==========================================================================
import math
import os
import random
import struct
import zlib
from multiprocessing import Pool

HERE = os.path.dirname(os.path.abspath(__file__))
WIDTH, HEIGHT = 2048, 1024

ZENITH = (0.10, 0.27, 0.66)      # 線形の色
HORIZON = (0.56, 0.72, 0.88)
GROUND_TOP = (0.30, 0.33, 0.36)
GROUND_BOTTOM = (0.09, 0.10, 0.11)
SUN_COLOR = (1.0, 0.93, 0.80)
SUN_AZIMUTH, SUN_ELEVATION = math.radians(40.0), math.radians(35.0)
SUN_DIR = (math.cos(SUN_ELEVATION) * math.sin(SUN_AZIMUTH), math.sin(SUN_ELEVATION), math.cos(SUN_ELEVATION) * math.cos(SUN_AZIMUTH))

# 値のノイズ (格子の点に乱数。滑らかに補間する。周期 256 で繰り返す)
SIZE = 256
random.seed(7)
LATTICE = [random.random() for _ in range(SIZE * SIZE)]


def value_noise(x, y):
    xi, yi = math.floor(x), math.floor(y)
    fx, fy = x - xi, y - yi
    fx = fx * fx * (3 - 2 * fx)
    fy = fy * fy * (3 - 2 * fy)
    x0, y0 = xi % SIZE, yi % SIZE
    x1, y1 = (x0 + 1) % SIZE, (y0 + 1) % SIZE
    a = LATTICE[y0 * SIZE + x0]
    b = LATTICE[y0 * SIZE + x1]
    c = LATTICE[y1 * SIZE + x0]
    d = LATTICE[y1 * SIZE + x1]
    return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy


def fbm(x, y):
    total, amplitude, frequency = 0.0, 0.5, 1.0
    for _ in range(5):
        total += amplitude * value_noise(x * frequency, y * frequency)
        amplitude *= 0.5
        frequency *= 2.03
    return total


def lerp3(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def shade(dx, dy, dz):
    if dy >= 0.0:
        # 空 : 地平から天頂へ (地平の近くで急に明るくなる)
        t = 1.0 - (1.0 - dy) ** 3.0
        color = lerp3(HORIZON, ZENITH, t)
        # 太陽の光の輪と丸
        cos_sun = dx * SUN_DIR[0] + dy * SUN_DIR[1] + dz * SUN_DIR[2]
        glow = max(cos_sun, 0.0) ** 64 * 0.9 + max(cos_sun, 0.0) ** 8 * 0.18
        disc = 1.0 if cos_sun > 0.99965 else max(0.0, (cos_sun - 0.9992) / 0.00045) if cos_sun > 0.9992 else 0.0
        color = (color[0] + SUN_COLOR[0] * (glow + disc * 6.0), color[1] + SUN_COLOR[1] * (glow + disc * 6.0), color[2] + SUN_COLOR[2] * (glow + disc * 6.0))
        # 雲 : 空の高さの板に映した位置のノイズ。地平の近くは薄く (遠くて霞む)
        if dy > 0.02:
            px, pz = dx / dy * 1.6, dz / dy * 1.6
            density = fbm(px * 1.3 + 31.7, pz * 1.3 + 12.9)
            coverage = max(0.0, min(1.0, (density - 0.47) * 3.2))
            fade = min(1.0, (dy - 0.02) / 0.18)
            coverage *= fade
            if coverage > 0.0:
                # 太陽の側は明るく、下側は少し灰色
                light = 0.82 + 0.18 * max(cos_sun, 0.0) + 0.1 * (fbm(px * 2.6 + 5.1, pz * 2.6 + 9.3) - 0.5)
                cloud = (light, light * 0.98, light * 0.96)
                color = lerp3(color, cloud, coverage * 0.92)
        return color
    # 地平より下 : 霞んだ地面 (地平の近くは空の色に溶ける)
    t = min(1.0, -dy * 2.2)
    ground = lerp3(GROUND_TOP, GROUND_BOTTOM, t ** 0.7)
    return lerp3(HORIZON, ground, min(1.0, -dy * 9.0))


def to_srgb8(c):
    out = []
    for v in c:
        v = max(0.0, v)
        v = v / (1.0 + v * 0.15)   # 明るすぎる太陽を少し丸める
        s = 12.92 * v if v <= 0.0031308 else 1.055 * (v ** (1.0 / 2.4)) - 0.055
        out.append(max(0, min(255, int(round(s * 255)))))
    return out


def render_row(y):
    row = bytearray(1 + WIDTH * 3)   # 先頭は PNG の行のフィルター (0)
    elevation = math.pi * (0.5 - (y + 0.5) / HEIGHT)
    cos_e, sin_e = math.cos(elevation), math.sin(elevation)
    for x in range(WIDTH):
        azimuth = 2.0 * math.pi * ((x + 0.5) / WIDTH - 0.5)
        dx, dy, dz = cos_e * math.sin(azimuth), sin_e, cos_e * math.cos(azimuth)
        r, g, b = to_srgb8(shade(dx, dy, dz))
        row[1 + x * 3] = r
        row[2 + x * 3] = g
        row[3 + x * 3] = b
    return bytes(row)


def png(rows):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    raw = b"".join(rows)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", WIDTH, HEIGHT, 8, 2, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def main():
    with Pool() as pool:
        rows = pool.map(render_row, range(HEIGHT), chunksize=8)
    path = os.path.join(HERE, "DefaultSky.png")
    with open(path, "wb") as f:
        f.write(png(rows))
    print(f"{path} ({os.path.getsize(path) // 1024} KB)")


if __name__ == "__main__":
    main()
