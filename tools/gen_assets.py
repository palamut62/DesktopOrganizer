"""Tema dokularini uretir (fircalanmis celik, plaka, kulp, ABS plastik, stud). Kullanim:
python tools/gen_assets.py   ->  src/Dolap.App/Assets/*.png
Kaynak script degisip yeniden uretilir; PNG'ler elle duzenlenmez."""
import os
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, gaussian_filter1d

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Dolap.App", "Assets")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(11)
L = np.array([-0.35, -0.75, 0.55]); L = L / np.linalg.norm(L)  # isik: sol-ust


def save(name, rgb, alpha=None):
    a = np.ones(rgb.shape[:2]) if alpha is None else alpha
    img = np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)])
    Image.fromarray((img * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, name))


def sdf_rrect(W, H, r, inset=0):
    y, x = np.mgrid[0:H, 0:W] + 0.5
    cx, cy = W / 2, H / 2
    hx, hy = W / 2 - inset - r, H / 2 - inset - r
    qx = np.abs(x - cx) - hx
    qy = np.abs(y - cy) - hy
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r


def aa(sdf):
    return np.clip(0.5 - sdf, 0, 1)


def brushed(W, H, sigma=40, wrap=True, fine=0.35):
    n = gaussian_filter1d(rng.normal(size=(H, W)), sigma, axis=1, mode="wrap" if wrap else "reflect")
    n /= n.std()
    g = gaussian_filter1d(rng.normal(size=(H, W)), 1.2, axis=1, mode="wrap" if wrap else "reflect")
    g /= g.std()
    return n * (1 - fine) + g * fine


def normals(h, k=1.0):
    gy, gx = np.gradient(h)
    n = np.dstack([-gx * k, -gy * k, np.ones_like(h)])
    return n / np.linalg.norm(n, axis=2, keepdims=True)


def smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def metal_front(W=448, H=144):
    sdf = sdf_rrect(W, H, 14, inset=2)
    depth = np.maximum(-sdf, 0)
    bn = brushed(W, H, 45)
    h = 9 * smooth(depth / 12) - 2.2 * np.exp(-(((depth - 24) / 2.2) ** 2)) + 0.25 * bn
    n = normals(h, 1.6)
    d = np.clip(n @ L, 0, 1)
    lum = 0.50 * (0.55 + 0.75 * d) + 0.035 * bn
    lum *= np.linspace(1.10, 0.84, H)[:, None]
    lum *= 0.55 + 0.45 * smooth(depth / 3)  # kenar AO
    lum += 0.18 * np.exp(-(((depth - 2.5) / 1.4) ** 2)) * (np.linspace(1, 0.2, H)[:, None])  # ince kenar parlamasi
    rgb = np.dstack([lum * 0.95, lum * 0.98, lum * 1.03])
    save("metal_front.png", rgb, aa(sdf))


def metal_plate(W=420, H=64):
    sdf = sdf_rrect(W, H, 7, inset=2)
    depth = np.maximum(-sdf, 0)
    bn = brushed(W, H, 30, wrap=False)
    h = 5 * smooth(depth / 6) - 1.5 * np.exp(-(((depth - 9) / 1.4) ** 2)) + 0.15 * bn
    y, x = np.mgrid[0:H, 0:W] + 0.5
    for sx in (20, W - 20):  # vidalar
        r = np.hypot(x - sx, y - H / 2)
        h += -3.5 * smooth((7 - r) / 4) * (r < 8)
        slot = (np.abs((x - sx) * 0.7 + (y - H / 2) * 0.7) < 1.1) & (r < 5)
        h -= 2.0 * slot
    n = normals(h, 1.5)
    d = np.clip(n @ L, 0, 1)
    lum = 0.74 * (0.60 + 0.62 * d) + 0.025 * bn
    lum *= np.linspace(1.05, 0.86, H)[:, None]
    lum *= 0.6 + 0.4 * smooth(depth / 2.5)
    rgb = np.dstack([lum * 0.97, lum * 0.99, lum * 1.02])
    save("metal_plate.png", rgb, aa(sdf))


def metal_handle(W=480, H=84):
    cy, r, x0, x1 = 34.0, 13.0, 70.0, 410.0
    y, x = np.mgrid[0:H, 0:W] + 0.5
    px = np.clip(x, x0, x1)
    vx, vy = x - px, y - cy
    dist = np.hypot(vx, vy)
    cap = aa(dist - r)
    nz = np.sqrt(np.clip(1 - (dist / r) ** 2, 0, 1))
    n = np.dstack([vx / r, vy / r, nz])
    t = np.clip(vy / r, -1, 1)
    d = np.clip(n @ np.array([0, -0.75, 0.66]), 0, 1)
    refl = 0.45 * np.exp(-(((t - 0.55) / 0.2) ** 2))
    spec = 0.95 * np.exp(-(((t + 0.5) / 0.09) ** 2)) * (0.6 + 0.4 * smooth((x - x0) / 60))
    bn = brushed(W, H, 30, wrap=False)
    lum = 0.22 + 0.55 * d + refl + spec * 0.6 + 0.02 * bn
    # tutucu ayaklar
    post = np.zeros((H, W))
    for px_ in (x0 + 4, x1 - 4):
        s = sdf_rrect(40, 52, 7)
        yy0, xx0 = int(cy - 26), int(px_ - 20)
        post[yy0:yy0 + 52, xx0:xx0 + 40] = np.maximum(post[yy0:yy0 + 52, xx0:xx0 + 40], aa(s))
    plum = np.full((H, W), 0.26) * (0.8 + 0.2 * np.clip(1 - (y - cy + 26) / 52, 0, 1))
    shadow = gaussian_filter(np.roll(cap, 8, axis=0), 5) * 0.55
    rgb_bar = np.dstack([lum * 0.97, lum * 0.99, lum * 1.03])
    rgb_post = np.dstack([plum, plum, plum * 1.05])
    # premultiplied: golge (siyah) < ayak < kulp
    a_total = 1 - (1 - shadow) * (1 - post) * (1 - cap)
    pm = rgb_bar * cap[..., None] + rgb_post * (post * (1 - cap))[..., None]
    out_rgb = np.clip(pm / np.maximum(a_total[..., None], 1e-4), 0, 1)
    save("metal_handle.png", out_rgb, a_total)


def metal_body(W=480, H=256):
    bn = brushed(W, H, 60)
    x = np.arange(W)[None, :] + 0.5
    edge = np.minimum(x, W - x)
    h = 6 * smooth(edge / 14) + 0.3 * bn
    h = np.broadcast_to(h, (H, W))
    n = normals(h, 1.4)
    d = np.clip(n @ L, 0, 1)
    sheen = 0.78 + 0.32 * np.exp(-(((x - W * 0.45) / (W * 0.30)) ** 2))
    lum = 0.30 * (0.5 + 0.8 * d) * sheen + 0.03 * bn
    lum *= 0.55 + 0.45 * smooth(edge / 6)
    save("metal_body.png", np.dstack([lum * 0.95, lum * 0.98, lum * 1.04]))


def tray(W=256, H=256):
    g = gaussian_filter(rng.normal(size=(H, W)), 0.7, mode="wrap")
    g /= g.std()
    y, x = np.mgrid[0:H, 0:W]
    weave = 0.5 * np.sin(x * np.pi / 2) * np.sin(y * np.pi / 2)
    lum = 0.075 + 0.014 * g + 0.006 * weave
    save("tray.png", np.dstack([lum, lum, lum * 1.04]))


def plastic_shade(h, k=1.5):
    n = normals(h, k)
    return np.clip(n @ L, 0, 1)


def block_front(W=448, H=144):
    sdf = sdf_rrect(W, H, 20, inset=2)
    depth = np.maximum(-sdf, 0)
    h = 10 * smooth(depth / 14)
    # kulp girintisi: ust-orta, alt koseleri yuvarlak
    y, x = np.mgrid[0:H, 0:W] + 0.5
    ns = sdf_rrect(190, 90, 34)  # merkezi 190x90, ust kismi gorunmez disarida kalir
    notch = ns[(np.clip((y - (-12)).astype(int), 0, 89)), np.clip((x - (W / 2 - 95)).astype(int), 0, 189)]
    inside = (x > W / 2 - 95) & (x < W / 2 + 95) & (y < 78)
    nd = np.where(inside, np.maximum(-notch, 0), 0)
    h -= 9 * smooth(nd / 8) * inside
    h = gaussian_filter(h, 0.8)
    d = plastic_shade(h, 1.8)
    s = d - plastic_shade(np.zeros_like(h) + 1e-9)  # duz yuzeye gore fark
    base = (L @ np.array([0, 0, 1.0]))
    delta = d - base
    hi = np.clip(delta * 1.9, 0, 1)
    lo = np.clip(-delta * 2.0, 0, 1)
    spec = 0.55 * np.exp(-(((depth - 3.0) / 1.5) ** 2)) * np.clip(1.2 - y / H, 0, 1) * (sdf < 0)
    # yumusak ust-sol parlama
    glow = 0.10 * np.clip(1 - (y / H), 0, 1)
    lo = np.clip(lo + 0.18 * np.clip((y / H - 0.55) * 2, 0, 1), 0, 1)
    alpha_w = np.clip(hi * 0.55 + spec + glow, 0, 0.85)
    alpha_b = np.clip(lo * 0.6, 0, 0.7)
    grain = 0.015 * gaussian_filter(rng.normal(size=(H, W)), 0.6)
    # beyaz ve siyah katmani tek RGBA'ya: once siyah, sonra beyaz (premultiplied karisim)
    a = 1 - (1 - alpha_w) * (1 - alpha_b)
    pm = np.dstack([alpha_w] * 3)  # beyaz katkisi
    rgb = np.where(a[..., None] > 1e-4, pm / np.maximum(a[..., None], 1e-4), 0)
    rgb = np.clip(rgb + grain[..., None], 0, 1)
    mask = aa(sdf)
    save("block_front.png", rgb, np.clip(a, 0, 1) * mask)


def block_body(W=480, H=256):
    bn = gaussian_filter(rng.normal(size=(H, W)), 0.7, mode="wrap")
    bn /= bn.std()
    x = np.arange(W)[None, :] + 0.5
    edge = np.minimum(x, W - x)
    h = np.broadcast_to(5 * smooth(edge / 10), (H, W)) + 0.05 * bn
    d = plastic_shade(h, 1.3)
    sheen = 0.85 + 0.3 * np.exp(-(((x - W * 0.4) / (W * 0.3)) ** 2))
    base = np.array([0.105, 0.215, 0.58])
    lum = (0.62 + 0.55 * d) * sheen * (1 + 0.012 * bn)
    save("block_body.png", np.dstack([base[0] * lum, base[1] * lum, base[2] * lum]))


def stud(W=128, H=84):
    S = 4
    w, h = W * S, H * S
    y, x = np.mgrid[0:h, 0:w] + 0.5
    cx, a, b, cyt, hgt = w / 2, 52 * S, 24 * S, 28 * S, 22 * S
    top = ((x - cx) / a) ** 2 + ((y - cyt) / b) ** 2 <= 1
    bot = ((x - cx) / a) ** 2 + ((y - (cyt + hgt)) / b) ** 2 <= 1
    side = ((np.abs(x - cx) <= a) & (y >= cyt) & (y <= cyt + hgt)) | bot
    mask = (top | side).astype(float)
    mask = gaussian_filter(mask, S * 0.6)[::S, ::S]
    y, x = np.mgrid[0:H, 0:W] + 0.5
    nx = np.clip((x - W / 2) / 52, -1, 1)
    nz = np.sqrt(1 - nx ** 2)
    sd = np.clip(-0.55 * nx + 0.8 * nz, 0, 1)
    base = np.array([0.13, 0.27, 0.68])
    side_l = (0.45 + 0.8 * sd)
    ty = (y - 28) / 24
    tx = (x - W / 2) / 52
    topmask = (tx ** 2 + ty ** 2 <= 1)
    top_l = 1.05 + 0.25 * np.clip(-tx * 0.5 - ty * 0.7, 0, 1) + 0.5 * np.exp(-(((tx + 0.35) ** 2 + (ty + 0.45) ** 2) / 0.05))
    ring = np.exp(-(((np.sqrt(np.clip(tx ** 2 + ty ** 2, 0, 9)) - 1.0) / 0.06) ** 2)) * 0.5
    lum = np.where(topmask, top_l - ring * 0.6, side_l)
    rgb = np.dstack([base[0] * lum, base[1] * lum, base[2] * lum])
    rgb = np.clip(rgb + np.where(topmask, 0, 0)[..., None], 0, 1)
    save("block_stud.png", rgb, mask)


if __name__ == "__main__":
    metal_front(); metal_plate(); metal_handle(); metal_body(); tray()
    block_front(); block_body(); stud()
    print("ok ->", os.path.abspath(OUT))
