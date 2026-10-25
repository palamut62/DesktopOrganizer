"""Native 3840x2400 satin desktop backgrounds, no room/weather layer or raster enlargement.
Run: python tools/gen_wallpapers.py. Deterministic source for packaged JPG assets.
"""

from pathlib import Path

import numpy as np
from PIL import Image

WIDTH, HEIGHT = 3840, 2400
OUTPUT = Path(__file__).resolve().parents[1] / "src" / "Dolap.App" / "Wallpapers"


def render(theme):
    rng = np.random.default_rng(62)
    y, x = np.mgrid[0:HEIGHT, 0:WIDTH].astype(np.float32)
    x /= WIDTH
    y /= HEIGHT
    # Broad studio illumination, without cloudy low-frequency noise or panel seams.
    light = np.exp(-(((x - 0.5) / 0.6) ** 2))
    wall = 0.065 + 0.052 * light * np.exp(-(((y - 0.28) / 0.52) ** 2))
    floor = 0.12 + 0.095 * light * np.exp(-(((y - 0.83) / 0.24) ** 2))
    transition = np.clip((y - 0.697) / 0.006, 0, 1)
    transition = transition * transition * (3 - 2 * transition)
    lum = wall * (1 - transition) + floor * transition
    lum -= 0.012 * np.exp(-(((y - 0.702) / 0.009) ** 2))
    # Subpixel grain is generated at the final resolution and also dithers gradients.
    lum += rng.normal(0, 0.0013, lum.shape).astype(np.float32)
    tint = (0.88, 0.95, 1.06) if theme == "metal" else (0.84, 0.94, 1.12)
    rgb = lum[..., None] * np.array(tint, dtype=np.float32)
    fade = np.sin(np.pi * x) ** 1.3
    for index in range(4):
        center = 0.235 + 0.048 * np.sin(x * np.pi * 2 - 1.1) + index * 0.004
        distance = (y - center) * HEIGHT
        line = np.exp(-((distance / 0.85) ** 2)) * (0.13 - index * 0.024)
        halo = np.exp(-((distance / 10) ** 2)) * 0.006
        rgb += ((line + halo) * fade)[..., None] * np.array((0.20, 0.61, 0.95))
    return np.uint8(np.clip(rgb * 255 + 0.5, 0, 255))


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for theme in ("metal", "blocks"):
        image = render(theme)
        for side in ("left", "right"):
            # Balanced lighting supports several independently positioned cabinets.
            Image.fromarray(image).save(OUTPUT / f"{theme}-{side}.jpg", quality=98, subsampling=0)
    print(f"Generated four native {WIDTH}x{HEIGHT} wallpapers in {OUTPUT}")


if __name__ == "__main__":
    main()
