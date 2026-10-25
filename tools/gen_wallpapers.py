"""Temaya uygun masaustu duvar kagitlari: dolabin durdugu 3B oda. Kullanim:
python tools/gen_wallpapers.py  ->  src/Dolap.App/Wallpapers/{metal,blocks}-{right,left}.jpg
Dolap sag kenardaysa isik havuzu sagda; sol surum aynalanir. Ciktilar elle duzenlenmez."""
import os
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, zoom

W, H = 3840, 2400
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Dolap.App", "Wallpapers")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(62)
Y, X = np.mgrid[0:H, 0:W].astype(np.float32)
HZ = H * 0.70          # duvar-zemin birlesimi
VX = W * 0.40          # kacis noktasi
CX = W * 0.855         # dolabin durdugu yer (sag surum)


def noise(scale, octaves=4):
    out = np.zeros((H, W), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        s = max(2, int(scale / (2 ** o)))
        small = rng.normal(size=(H // s + 2, W // s + 2)).astype(np.float32)
        big = zoom(small, s, order=3)[:H, :W]
        out += amp * big; total += amp; amp *= 0.5
    return out / total


def smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def floor_coords():
    d = np.maximum(Y - HZ, 1.0)
    z = 2600.0 / d                         # derinlik
    xw = (X - VX) * z / 900.0              # zemin dunya x
    return z, xw


def finish(rgb, name):
    for side in ("right", "left"):
        img = rgb if side == "right" else rgb[:, ::-1]
        im = Image.fromarray((np.clip(img, 0, 1) ** (1 / 1.7) * 255 + 0.5).astype(np.uint8), "RGB")
        im.save(os.path.join(OUT, f"{name}-{side}.jpg"), quality=93, optimize=True)


def lighting(cx):
    wall = Y < HZ
    # Duvarda tavandan inen spot konisi + zeminde elips isik havuzu
    cone = np.exp(-(((X - cx) / (W * 0.13 + (Y / HZ) * W * 0.05)) ** 2)) * smooth(Y / HZ * 1.2) * wall
    pool = np.exp(-(((X - cx) / (W * 0.20)) ** 2 + ((Y - (HZ + H * 0.13)) / (H * 0.10)) ** 2)) * (~wall)
    # Dolabin zemindeki temas golgesi
    shadow = np.exp(-(((X - cx) / (W * 0.085)) ** 2 + ((Y - (HZ + H * 0.07)) / (H * 0.035)) ** 2))
    ao = np.exp(-((Y - HZ) / (H * 0.018)) ** 2)            # duvar-zemin kosesi
    vig = 1 - 0.55 * (((X - W / 2) / (W * 0.62)) ** 2 + ((Y - H * 0.5) / (H * 0.75)) ** 2)
    return cone, pool, shadow, ao, np.clip(vig, 0.25, 1)


def metal():
    cone, pool, shadow, ao, vig = lighting(CX)
    wall = Y < HZ
    conc = noise(220) * 0.5 + noise(40, 3) * 0.35 + rng.normal(size=(H, W)).astype(np.float32) * 0.08
    base_wall = 0.030 + 0.010 * conc
    # Duvarda yatay beton kalip izleri
    seams = np.exp(-(((Y % (H * 0.175)) - 2) / 2.5) ** 2) * 0.012
    z, xw = floor_coords()
    tile = np.minimum(np.abs((xw % 1.0) - 0.5), 0.5) ; tz = np.abs(((z * 0.9) % 1.0) - 0.5)
    seam_f = (np.exp(-((0.5 - tile) / 0.004) ** 2) + np.exp(-((0.5 - tz) / (0.004 * z)) ** 2)) * np.clip(2.2 / z, 0, 1)
    base_floor = 0.042 + 0.006 * noise(60, 3) - 0.012 * seam_f  # golgeler gorunsun diye zemin biraz aydinlik
    # Cilali zemin: duvarin bulanik yansimasi
    refl_src = np.where(wall, base_wall + 0.22 * cone, 0)
    mirror = refl_src[np.clip((2 * HZ - Y).astype(int), 0, H - 1), X.astype(int)]
    mirror = gaussian_filter(mirror, 18) * 0.35
    lum = np.where(wall, base_wall - seams + 0.20 * cone, base_floor + mirror + 0.17 * pool)
    lum = lum * (1 - 0.55 * ao)  * vig
    warm = np.dstack([lum * 1.06, lum * 1.0, lum * 0.92])
    cool = np.dstack([lum * 0.9, lum * 0.97, lum * 1.08])
    mix = (0.6 * cone + 0.6 * pool)[..., None]
    rgb = cool * (1 - mix) + warm * mix
    # Ince mavi isik cizgisi (marka dalgasi), duvarda
    wave_y = HZ * 0.52 + np.sin(X / W * np.pi * 2.2 + 0.6) * H * 0.045
    glow = np.exp(-((Y - wave_y) / 5.0) ** 2) * 0.9 + np.exp(-((Y - wave_y) / 60.0) ** 2) * 0.10
    glow *= smooth((X - W * 0.05) / (W * 0.3)) * smooth((W * 0.80 - X) / (W * 0.15)) * wall
    rgb += np.dstack([glow * 0.05, glow * 0.25, glow * 0.85])
    finish(rgb, "metal")


def blocks():
    cone, pool, shadow, ao, vig = lighting(CX)
    wall = Y < HZ
    plaster = noise(160) * 0.5 + rng.normal(size=(H, W)).astype(np.float32) * 0.05
    base_wall = 0.022 + 0.006 * plaster
    z, xw = floor_coords()
    # Zemin: dev taban plakasi; perspektifte stud (cikinti) halkalari
    u = (xw * 3.0) % 1.0 - 0.5; v = (z * 2.7) % 1.0 - 0.5
    r = np.sqrt(u * u + (v * 1.0) ** 2)
    stud_top = smooth((0.30 - r) / 0.03)
    rim = np.exp(-((r - 0.31) / 0.025) ** 2)
    fade = np.clip(1.8 / z, 0, 1)
    base_floor = 0.038 + (0.010 * stud_top - 0.010 * rim * (v > 0) + 0.012 * rim * (v < 0)) * fade
    refl_src = np.where(wall, base_wall + 0.18 * cone, 0)
    mirror = gaussian_filter(refl_src[np.clip((2 * HZ - Y).astype(int), 0, H - 1), X.astype(int)], 22) * 0.25
    lum = np.where(wall, base_wall + 0.17 * cone, base_floor + mirror + 0.15 * pool)
    lum = lum * (1 - 0.5 * ao)  * vig
    rgb = np.dstack([lum * 0.55, lum * 0.80, lum * 1.55])
    # Belirgin mavi isik dalgasi (konseptteki gibi), iki katman
    for amp, ph, k, w_ in ((0.06, 0.4, 2.0, 1.0), (0.035, 1.9, 2.8, 0.55)):
        wy = HZ * 0.55 + np.sin(X / W * np.pi * k + ph) * H * amp
        g = (np.exp(-((Y - wy) / 6.0) ** 2) + np.exp(-((Y - wy) / 70.0) ** 2) * 0.22) * w_ * wall
        g *= smooth((W * 0.82 - X) / (W * 0.2))
        rgb += np.dstack([g * 0.08, g * 0.32, g * 1.0])
    # Sicak kenar isiklari (zeminde renkli yansima)
    for col, xx in (((1.0, 0.45, 0.1), 0.80), ((0.2, 0.9, 0.3), 0.90)):
        sp = np.exp(-(((X - W * xx) / (W * 0.03)) ** 2 + ((Y - H * 0.93) / (H * 0.05)) ** 2)) * 0.10 * (~wall)
        rgb += np.dstack([sp * col[0], sp * col[1], sp * col[2]])
    finish(rgb, "blocks")


if __name__ == "__main__":
    metal(); blocks()
    print("ok ->", os.path.abspath(OUT))
