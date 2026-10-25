"""Ofis odasi duvar kagidi katmanlari (calisma aninda C# birlestirir). Kullanim:
python tools/gen_office.py  ->  src/Dolap.App/Office/
  base-{blocks,metal}-{day,night}-{win,nowin}.png  temaya uygun oda: LEGO tuglali / endustriyel beton (camlar seffaf)
  sky-{clear,cloudy,rain,snow}-{day,dusk,night}.png  pencereden gorunen gokyuzu + sehir
  lights.png  tavan isiklari acikken eklenen isik;  sunbeam.png  gunesli gunde iceri sizan isik
Zemin-duvar cizgisi ekranin %70'inde (MainWindow.DepthFactor bununla uyumlu). Ciktilar elle duzenlenmez."""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import gaussian_filter, zoom

W, H = 2560, 1600
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Dolap.App", "Office")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(2026)
Y, X = np.mgrid[0:H, 0:W].astype(np.float32)

# Tek kacis noktali oda: arka duvar dikdortgeni, yan duvarlar/tavan/zemin yamuk.
BX0, BX1, BY0, BY1 = 0.13 * W, 0.87 * W, 0.11 * H, 0.70 * H
VX, VY = 0.5 * W, 0.42 * H
WIN = (int(0.19 * W), int(0.19 * H), int(0.47 * W), int(0.55 * H))  # pencere disi (x0,y0,x1,y1)


def noise(scale, octaves=4, shape=(H, W)):
    h, w = shape
    out = np.zeros(shape, np.float32); amp = 1.0; tot = 0.0
    for _ in range(octaves):
        s = max(2, int(scale))
        small = rng.normal(size=(h // s + 3, w // s + 3)).astype(np.float32)
        out += amp * zoom(small, s, order=3)[:h, :w]
        tot += amp; amp *= 0.5; scale /= 2
    return out / tot


def smooth(t):
    t = np.clip(t, 0, 1); return t * t * (3 - 2 * t)


def save(name, rgb, alpha=None):
    a = np.ones(rgb.shape[:2], np.float32) if alpha is None else alpha
    img = np.dstack([np.clip(rgb, 0, 1) ** (1 / 2.2), np.clip(a, 0, 1)])
    Image.fromarray((img * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OUT, name), optimize=True)


def region_masks():
    # Yamuk bolgeler: x sinirlari y'ye gore dogrusal
    def lerp(a, b, t): return a + (b - a) * t
    back = (X >= BX0) & (X <= BX1) & (Y >= BY0) & (Y <= BY1)
    t_floor = np.clip((Y - BY1) / (H - BY1), 0, 1)
    floor = (Y > BY1) & (X >= lerp(BX0, 0, t_floor)) & (X <= lerp(BX1, W, t_floor))
    t_ceil = np.clip((BY0 - Y) / BY0, 0, 1)
    ceil = (Y < BY0) & (X >= lerp(BX0, 0, t_ceil)) & (X <= lerp(BX1, W, t_ceil))
    left = (X < BX0) & ~floor & ~ceil
    right = (X > BX1) & ~floor & ~ceil
    return back, floor, ceil, left, right


def side_uv():
    """Yan duvarlar icin perspektife uygun (u, v): u derinlik, v arka duvar yuksekligine olceklenmis."""
    t = np.where(X < BX0, X / BX0, (W - X) / (W - BX1))
    ytop = BY0 * t; ybot = H - (H - BY1) * t
    v = BY0 + (Y - ytop) / np.maximum(ybot - ytop, 1) * (BY1 - BY0)
    u = 2600 * (1 - t) / (0.35 + 0.65 * t)
    return u, v


def bricks(u, v, bh, palette, weights, seed):
    """LEGO tugla duvari (yandan 2x4 tugla): sasirtmali siralar, derz, ust kenar parlakligi, alt golge."""
    bw = bh * 3.33
    row = np.floor(v / bh)
    off = (row % 2) * bw / 2
    col = np.floor((u + off) / bw)
    h = np.abs((np.sin(row * 12.9898 + col * 78.233 + seed) * 43758.5453) % 1)
    cum = np.cumsum(weights) / np.sum(weights)
    idx = np.searchsorted(cum, h)
    pal = np.array(palette)[np.clip(idx, 0, len(palette) - 1)]
    fx = ((u + off) % bw) / bw; fy = (v % bh) / bh
    shade = 1 + 0.10 * (fy < 0.14) - 0.12 * (fy > 0.86) + 0.05 * (1 - fy)
    seam = (fx < 0.015) | (fy < 0.035)
    return pal * (shade * (1 - 0.55 * seam))[..., None]


def room(window: bool, night: bool, theme: str):
    back, floor, ceil, left, right = region_masks()
    side = left | right
    lum = np.zeros((H, W, 3), np.float32)
    wx = (WIN[0] + WIN[2]) / 2
    win_glow = np.exp(-(((X - wx) / (W * 0.30)) ** 2 + ((Y - 0.40 * H) / (H * 0.35)) ** 2)) if window else 0
    shade_back = 0.80 + 0.22 * win_glow - 0.15 * ((Y - BY0) / (BY1 - BY0)) ** 2
    depth_side = np.where(left, X / BX0, (W - X) / (W - BX1))
    su, sv = side_uv()
    if theme == "blocks":
        # LEGO tuglalari: acik mavimsi gri, bej ve beyaz karisimi
        pal = [(0.62, 0.64, 0.66), (0.80, 0.72, 0.55), (0.90, 0.90, 0.88)]
        wts = [6, 2.5, 1.5]
        bh = 0.026 * H
        wall_back = bricks(X, Y, bh, pal, wts, 1.0)
        # uzak yan duvarda tuglalar piksel altina iner: hafif bulaniklik karincalanmayi onler
        wall_side = np.stack([gaussian_filter(ch, 1.8) for ch in np.moveaxis(bricks(su, sv, bh, pal, wts, 7.0), 2, 0)], axis=2)
        skirt_col = np.array([0.05, 0.16, 0.45])
    else:
        # endustriyel: brut beton paneller, kalip bagi delikleri
        c = (0.52 + 0.05 * noise(140, 4) + 0.025 * noise(10, 2))[..., None] * np.array([1.0, 1.01, 1.03])
        pw, ph = (BX1 - BX0) / 6, (BY1 - BY0) / 3
        pu, pv = (X - BX0) % pw, (Y - BY0) % ph
        seams = (pu < 3) | (pv < 3)
        holes = np.zeros((H, W), bool)
        for hx in (0.25, 0.75):
            for hy in (0.25, 0.75):
                holes |= np.hypot(pu - hx * pw, pv - hy * ph) < 7
        wall_back = c * (1 - 0.35 * seams - 0.45 * holes)[..., None]
        wall_side = c * 0.95
        skirt_col = np.array([0.24, 0.25, 0.27])
    lum += back[..., None] * wall_back * shade_back[..., None]
    lum += side[..., None] * wall_side * (0.48 + 0.12 * depth_side)[..., None]
    skirt = back & (Y > BY1 - (0.03 if theme == "blocks" else 0.018) * H)
    lum[skirt] = skirt_col
    # tavan: karo izgara + gomme isik panelleri (kapali)
    tc = np.clip((BY0 - Y) / BY0, 1e-3, 1)
    cz = 1 / tc
    cxw = (X - VX) / (VY - Y + 1e-3) * (VY)
    grid = (np.abs(((cxw / 220) % 1) - 0.5) > 0.485) | (np.abs(((cz * 1.6) % 1) - 0.5) > 0.47)
    base_ceil = np.array([0.88, 0.88, 0.86]) if theme == "blocks" else np.array([0.34, 0.35, 0.36])
    lum += ceil[..., None] * base_ceil * (0.62 + 0.18 * (1 - tc))[..., None]
    lum[ceil & grid] *= 0.82
    panels = np.zeros((H, W), bool)
    for px in (-1.1, 0, 1.1):
        u = cxw / 260 - px
        panels |= ceil & (np.abs(u) < 0.32) & (np.abs(((cz * 1.6) % 2) - 1.0) < 0.38)
    lum[panels] = np.array([0.80, 0.82, 0.84])
    # zemin
    d = np.maximum(Y - BY1, 1)
    z = 900 / d
    xw = (X - VX) * z / 600
    near = 0.70 + 0.30 * smooth((Y - BY1) / (H - BY1))
    if theme == "blocks":
        # taban plakasi: acik gri, perspektifte stud halkalari
        uu = (xw * 3.0) % 1 - 0.5; vv = (z * 2.7) % 1 - 0.5
        r = np.sqrt(uu * uu + vv * vv)
        fade = np.clip(1.6 / z, 0, 1)
        top = smooth((0.30 - r) / 0.03)
        rim = np.exp(-((r - 0.31) / 0.03) ** 2)
        fl = (0.60 + (0.06 * top + 0.10 * rim * (vv < 0) - 0.12 * rim * (vv > 0)) * fade) * near
        floor_col = np.array([0.58, 0.61, 0.64])
        refl_k = 0.06
    else:
        # cilali beton: buyuk plakalar, derzler
        seam = (np.abs((xw * 1.2) % 1 - 0.5) > 0.493) | (np.abs((z * 0.6) % 1 - 0.5) > 0.49)
        fl = (0.55 + 0.05 * noise(80, 3) + 0.02 * noise(12, 2)) * near * (1 - 0.30 * seam)
        floor_col = np.array([0.50, 0.51, 0.53])
        refl_k = 0.22
    lum += floor[..., None] * floor_col * fl[..., None]
    refl = gaussian_filter(np.where(back, shade_back, 0), 25)[np.clip((2 * BY1 - Y).astype(int), 0, H - 1), X.astype(int)]
    lum += (floor * refl * refl_k)[..., None] * np.array([0.8, 0.8, 0.8])
    # Kose golgesi yalniz gercek birlesim cizgileri boyunca (duvar-zemin, duvar-tavan, kose dikmeleri, capraz yan kenarlar).
    labels = back * 1 + floor * 2 + ceil * 3 + left * 4 + right * 5
    boundary = np.zeros((H, W), np.float32)
    boundary[:, 1:] += labels[:, 1:] != labels[:, :-1]
    boundary[1:, :] += labels[1:, :] != labels[:-1, :]
    ao = gaussian_filter(np.clip(boundary, 0, 1), 0.012 * H)
    ao = np.clip(ao / ao.max() * 1.4, 0, 1)
    lum *= (1 - 0.35 * ao)[..., None]
    alpha = np.ones((H, W), np.float32)
    if window:
        x0, y0, x1, y1 = WIN
        frame = (X >= x0) & (X <= x1) & (Y >= y0) & (Y <= y1)
        if theme == "blocks":
            fr, mull = int(0.020 * W), int(0.010 * W)
            cols, rows = [0.5], [0.42]
            frame_col = np.array([0.72, 0.06, 0.04])          # LEGO kirmizisi, kalin cerceve
            sill_col = np.array([0.95, 0.74, 0.06])           # sari esik
        else:
            fr, mull = int(0.009 * W), int(0.0035 * W)
            cols, rows = [0.25, 0.5, 0.75], [0.33, 0.66]      # fabrika penceresi izgarasi
            frame_col = np.array([0.07, 0.075, 0.08])
            sill_col = np.array([0.30, 0.31, 0.33])
        glass = (X >= x0 + fr) & (X <= x1 - fr) & (Y >= y0 + fr) & (Y <= y1 - fr)
        for cx in cols:
            glass &= np.abs(X - (x0 + (x1 - x0) * cx)) > mull
        for cy in rows:
            glass &= np.abs(Y - (y0 + (y1 - y0) * cy)) > mull
        grad = (0.85 + 0.30 * (1 - (Y - y0) / (y1 - y0)))[..., None]
        lum[frame] = (frame_col * grad)[frame]
        if theme == "blocks":
            edge_hl = frame & ~glass & ((np.abs(X - x0) < 4) | (np.abs(Y - y0) < 4))
            lum[edge_hl] = np.minimum(lum[edge_hl] * 1.6, 1)
        sill = (X >= x0 - fr) & (X <= x1 + fr) & (Y > y1) & (Y < y1 + 0.018 * H)
        lum[sill] = sill_col
        if theme == "blocks":  # esik ustunde stud siralari
            for sxp in np.arange(x0, x1 + fr, 0.012 * W):
                stud = (np.abs(X - sxp) < 0.004 * W) & (Y > y1 - 0.006 * H) & (Y <= y1)
                lum[stud] = sill_col * 1.1
        lum[(X >= x0 - fr) & (X <= x1 + fr) & (Y >= y1 + 0.018 * H) & (Y < y1 + 0.024 * H)] *= 0.45
        streak = np.exp(-(((X - x0) - (Y - y0) * 0.6 - 0.22 * (x1 - x0)) / (0.06 * W)) ** 2) * 0.10
        alpha = np.where(glass, streak, 1.0)
        lum[glass] = 1.0
    if night:
        lum = lum * np.array([0.16, 0.18, 0.26])
    return lum, alpha


def sky(weather, phase):
    x0, y0, x1, y1 = WIN
    w, h = x1 - x0, y1 - y0
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    t = yy / h
    top, bot = {
        ("day", "clear"): ((0.20, 0.45, 0.85), (0.65, 0.80, 0.95)),
        ("day", "cloudy"): ((0.48, 0.53, 0.60), (0.72, 0.75, 0.79)),
        ("day", "rain"): ((0.30, 0.33, 0.38), (0.50, 0.53, 0.57)),
        ("day", "snow"): ((0.62, 0.65, 0.70), (0.85, 0.86, 0.88)),
        ("dusk", "clear"): ((0.18, 0.20, 0.45), (0.98, 0.55, 0.30)),
        ("dusk", "cloudy"): ((0.22, 0.20, 0.30), (0.75, 0.45, 0.35)),
        ("dusk", "rain"): ((0.14, 0.14, 0.20), (0.45, 0.32, 0.30)),
        ("dusk", "snow"): ((0.30, 0.28, 0.40), (0.80, 0.62, 0.58)),
        ("night", "clear"): ((0.01, 0.02, 0.06), (0.05, 0.08, 0.18)),
        ("night", "cloudy"): ((0.03, 0.03, 0.05), (0.09, 0.09, 0.12)),
        ("night", "rain"): ((0.02, 0.02, 0.04), (0.06, 0.07, 0.09)),
        ("night", "snow"): ((0.05, 0.05, 0.08), (0.14, 0.14, 0.18)),
    }[(phase, weather)]
    rgb = np.array(top) * (1 - t[..., None]) + np.array(bot) * t[..., None]
    # gunes / ay / yildizlar
    if weather == "clear":
        if phase == "day":
            r = np.hypot(xx - w * 0.72, yy - h * 0.22)
            rgb += (np.exp(-(r / (0.05 * w)) ** 2) * 1.2 + np.exp(-(r / (0.25 * w)) ** 2) * 0.25)[..., None] * np.array([1, 0.95, 0.8])
        if phase == "dusk":
            r = np.hypot(xx - w * 0.25, yy - h * 0.62)
            rgb += (np.exp(-(r / (0.06 * w)) ** 2) * 1.0 + np.exp(-(r / (0.35 * w)) ** 2) * 0.35)[..., None] * np.array([1, 0.6, 0.3])
        if phase == "night":
            stars = (rng.random((h, w)) > 0.9985) * rng.uniform(0.4, 1, (h, w))
            rgb += gaussian_filter(stars, 0.7)[..., None] * 3
            r = np.hypot(xx - w * 0.75, yy - h * 0.2)
            rgb += (np.exp(-(r / (0.035 * w)) ** 6) * 0.9 + np.exp(-(r / (0.12 * w)) ** 2) * 0.08)[..., None] * np.array([0.95, 0.95, 1.0])
    # bulutlar
    density = {"clear": 0.0, "cloudy": 0.75, "rain": 0.95, "snow": 0.85}[weather]
    if weather == "clear" and phase != "night": density = 0.25
    if density > 0:
        n = noise(90, 5, (h, w)); n = (n - n.min()) / (n.max() - n.min())
        cover = smooth((n - (1 - density) * 0.9) / 0.25) * (1 - 0.4 * t)
        cloud_col = {"day": (0.92, 0.93, 0.95), "dusk": (0.95, 0.62, 0.50), "night": (0.10, 0.10, 0.13)}[phase]
        if weather in ("rain",): cloud_col = tuple(c * 0.6 for c in cloud_col)
        shade = 0.75 + 0.25 * gaussian_filter(n, 6)
        rgb = rgb * (1 - cover[..., None]) + np.array(cloud_col) * (cover * shade)[..., None]
    # sehir silueti (ofisten bakis)
    sky_rgb = rgb.copy()
    city = np.zeros((h, w), bool); lit = np.zeros((h, w), np.float32)
    x = 0
    while x < w:
        bw = int(rng.uniform(0.05, 0.13) * w); bh = rng.uniform(0.18, 0.48) * h
        city[int(h - bh):, x:x + bw] = True
        if phase != "day":
            for wy in range(int(h - bh + 8), h - 6, 14):
                for wx in range(x + 6, x + bw - 6, 11):
                    if rng.random() < 0.35: lit[wy:wy + 6, wx:wx + 5] = rng.uniform(0.6, 1)
        x += bw + int(rng.uniform(0, 0.02) * w)
    haze = {"day": 0.35, "dusk": 0.18, "night": 0.05}[phase]
    bcol = np.array({"day": (0.42, 0.46, 0.52), "dusk": (0.20, 0.17, 0.22), "night": (0.03, 0.035, 0.05)}[phase])
    bcol = bcol * (1 - haze) + np.array(bot) * haze
    rgb[city] = bcol
    rgb += (lit * city)[..., None] * np.array([1.0, 0.82, 0.45]) * 0.9
    # yagis
    if weather == "rain":
        streaks = np.zeros((h, w), np.float32)
        for _ in range(900):
            sx, sy, L = rng.integers(0, w), rng.integers(0, h), rng.integers(15, 40)
            for k in range(L):
                px, py = sx + k * 0.25, sy + k
                if 0 <= int(px) < w and 0 <= int(py) < h: streaks[int(py), int(px)] = 1
        rgb += gaussian_filter(streaks, 0.6)[..., None] * (0.35 if phase == "day" else 0.15)
    if weather == "snow":
        flakes = (rng.random((h, w)) > 0.996).astype(np.float32)
        rgb += gaussian_filter(flakes, 1.2)[..., None] * 4.0
    return np.clip(rgb, 0, 1.3)


def lights():
    """Tavan panelleri yanik + duvar ve zeminde isik havuzlari (sicak beyaz)."""
    back, floor, ceil, left, right = region_masks()
    a = np.zeros((H, W), np.float32)
    tc = np.clip((BY0 - Y) / BY0, 1e-3, 1); cz = 1 / tc
    cxw = (X - VX) / (VY - Y + 1e-3) * VY
    for px in (-1.1, 0, 1.1):
        u = cxw / 260 - px
        panel = ceil & (np.abs(u) < 0.32) & (np.abs(((cz * 1.6) % 2) - 1.0) < 0.38)
        a = np.maximum(a, panel * 0.95)
    glow = gaussian_filter(a, 18) * 1.6
    pools = np.zeros((H, W), np.float32)
    for px in (0.25, 0.5, 0.75):
        pools += np.exp(-(((X - px * W) / (0.14 * W)) ** 2 + ((Y - 0.30 * H) / (0.22 * H)) ** 2)) * back * 0.32
        pools += np.exp(-(((X - px * W) / (0.20 * W)) ** 2 + ((Y - 0.86 * H) / (0.12 * H)) ** 2)) * floor * 0.30
    alpha = np.clip(a + glow * 0.8 + pools, 0, 0.9)
    rgb = np.ones((H, W, 3), np.float32) * np.array([1.0, 0.93, 0.80])
    save("lights.png", rgb, alpha)


def sunbeam():
    """Pencereden giren gunes isiginin zemindeki izi: duvarin onunde, pencere bicimli, gunesin ters yonune kaymis
    (gunes gokyuzunde sag-ustte) ve perspektifte izleyiciye dogru genisleyen dortgen; cita golgeleri dahil."""
    x0, y0, x1, y1 = WIN
    img = Image.new("L", (W, H), 0); d = ImageDraw.Draw(img)

    def floor_pt(wx, depth):
        """Arka duvar dibindeki x ve duvardan uzaklik (0..1) -> ekran noktasi (tek kacis noktali perspektif)."""
        ys = BY1 + (H - BY1) * depth
        k = (ys - VY) / (BY1 - VY)
        return (VX + (wx - VX) * k, ys)

    shift = -0.07 * W                     # gunes sagda: iz sola kayar
    near, far = 0.06, 0.62                # pencere yuksekligi / gunes acisi -> zemindeki derinlik araligi
    pts = [floor_pt(x0 + shift * 0.4, near), floor_pt(x1 + shift * 0.4, near), floor_pt(x1 + shift, far), floor_pt(x0 + shift, far)]
    d.polygon(pts, fill=105)
    # cita golgeleri (dikey ve yatay kayit)
    mx = (x0 + x1) / 2
    d.line([floor_pt(mx + shift * 0.4, near), floor_pt(mx + shift, far)], fill=35, width=int(0.010 * W))
    midd = near + (far - near) * 0.42
    d.line([floor_pt(x0 + shift * 0.65, midd), floor_pt(x1 + shift * 0.65, midd)], fill=35, width=int(0.008 * W))
    img = img.filter(ImageFilter.GaussianBlur(14))
    a = np.asarray(img, np.float32) / 255
    # havada asili toz/isik huzmesi: pencereden ize uzanan cok hafif bant
    beam = Image.new("L", (W, H), 0); db = ImageDraw.Draw(beam)
    db.polygon([(x0, y0 + (y1 - y0) * 0.1), (x1, y0 + (y1 - y0) * 0.1), pts[2], pts[3]], fill=18)
    a = np.maximum(a, np.asarray(beam.filter(ImageFilter.GaussianBlur(60)), np.float32) / 255)
    rgb = np.ones((H, W, 3), np.float32) * np.array([1.0, 0.86, 0.62])
    save("sunbeam.png", rgb, a)


if __name__ == "__main__":
    for theme in ("blocks", "metal"):
        for night in (False, True):
            for window in (True, False):
                lum, alpha = room(window, night, theme)
                save(f"base-{theme}-{'night' if night else 'day'}-{'win' if window else 'nowin'}.png", lum, alpha)
    for wth in ("clear", "cloudy", "rain", "snow"):
        for ph in ("day", "dusk", "night"):
            s = sky(wth, ph)
            Image.fromarray((np.clip(s, 0, 1) ** (1 / 2.2) * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, f"sky-{wth}-{ph}.png"), optimize=True)
    lights(); sunbeam()
    print("ok ->", os.path.abspath(OUT), "WIN=", WIN)
