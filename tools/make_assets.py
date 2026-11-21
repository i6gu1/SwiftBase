"""
Build SwiftBase icon assets from icon.jpg.

The source artwork is a wide banner whose background is a dark checkerboard.
This script:
  1. separates the emblem from the checkerboard (colour key + cleanup),
  2. trims + pads it to a square and puts it on a dark rounded tile,
  3. writes assets/icon.png (1024) and assets/icon.ico (multi-size),
  4. writes branded Inno Setup wizard images.
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "icon.jpg"
ASSETS = ROOT / "assets"
INSTALLER = ROOT / "installer"

BG = (9, 14, 21)  # SwiftBase deep navy, used behind the artwork


def flood_from_border(region: np.ndarray) -> np.ndarray:
    """Return the pixels of `region` reachable from the image border."""
    h, w = region.shape
    seen = np.zeros((h, w), dtype=bool)
    stack: list[tuple[int, int]] = []

    def seed(y: int, x: int) -> None:
        if region[y, x] and not seen[y, x]:
            seen[y, x] = True
            stack.append((y, x))

    for x in range(w):
        seed(0, x)
        seed(h - 1, x)
    for y in range(h):
        seed(y, 0)
        seed(y, w - 1)

    while stack:
        y, x = stack.pop()
        if y > 0 and region[y - 1, x] and not seen[y - 1, x]:
            seen[y - 1, x] = True
            stack.append((y - 1, x))
        if y + 1 < h and region[y + 1, x] and not seen[y + 1, x]:
            seen[y + 1, x] = True
            stack.append((y + 1, x))
        if x > 0 and region[y, x - 1] and not seen[y, x - 1]:
            seen[y, x - 1] = True
            stack.append((y, x - 1))
        if x + 1 < w and region[y, x + 1] and not seen[y, x + 1]:
            seen[y, x + 1] = True
            stack.append((y, x + 1))

    return seen


def largest_component(solid: np.ndarray) -> np.ndarray:
    """Keep only the biggest 4-connected shape (the shield emblem)."""
    h, w = solid.shape
    seen = np.zeros((h, w), dtype=bool)
    best_area = 0
    best_points: list[tuple[int, int]] = []

    for y0 in range(h):
        for x0 in range(w):
            if not solid[y0, x0] or seen[y0, x0]:
                continue
            seen[y0, x0] = True
            stack = [(y0, x0)]
            points: list[tuple[int, int]] = []
            while stack:
                y, x = stack.pop()
                points.append((y, x))
                for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                    if 0 <= ny < h and 0 <= nx < w and solid[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            if len(points) > best_area:
                best_area = len(points)
                best_points = points

    out = np.zeros((h, w), dtype=np.uint8)
    ys = np.fromiter((p[0] for p in best_points), dtype=np.int32, count=len(best_points))
    xs = np.fromiter((p[1] for p in best_points), dtype=np.int32, count=len(best_points))
    out[ys, xs] = 255
    return out


def emblem(src: Image.Image) -> Image.Image:
    rgb = np.array(src)
    r, g, b = rgb[..., 0].astype(np.int16), rgb[..., 1].astype(np.int16), rgb[..., 2].astype(np.int16)
    lum = r * 0.299 + g * 0.587 + b * 0.114

    # The checkerboard is a desaturated blue-grey; the artwork's navy is much
    # bluer. This key removes the checker while keeping black outlines intact.
    background = ((b - 1.5 * g) < 5) & (lum < 65)

    foreground = Image.fromarray(((~background) * 255).astype(np.uint8), "L")
    foreground = foreground.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
    foreground = foreground.filter(ImageFilter.MaxFilter(7)).filter(ImageFilter.MinFilter(7))
    shape = np.asarray(foreground) > 0

    # Fill inner holes (the dark body of the shield and mug).
    outside = flood_from_border(~shape)
    solid = shape | (~outside)

    mask = largest_component(solid)
    alpha = Image.fromarray(mask, "L").filter(ImageFilter.GaussianBlur(0.8))
    alpha = alpha.point(lambda v: 0 if v < 35 else min(255, int(v * 1.35)))
    bbox = alpha.getbbox()
    if not bbox:
        raise SystemExit("Could not detect the logo inside icon.jpg")

    alpha = alpha.crop(bbox)
    logo = src.convert("RGBA").crop(bbox)
    logo.putalpha(alpha)
    return logo


def square(logo: Image.Image, canvas: int, pad_ratio: float, background=None) -> Image.Image:
    inner = int(canvas * (1 - pad_ratio * 2))
    ratio = min(inner / logo.width, inner / logo.height)
    size = (max(1, round(logo.width * ratio)), max(1, round(logo.height * ratio)))
    resized = logo.resize(size, Image.LANCZOS)

    mode = "RGBA" if background is None else "RGB"
    out = Image.new(mode, (canvas, canvas), background or (0, 0, 0, 0))
    out.paste(resized, ((canvas - size[0]) // 2, (canvas - size[1]) // 2), resized)
    return out


def rounded_card(size: int) -> Image.Image:
    """Dark rounded-square app tile with a soft blue glow."""
    glow = Image.new("L", (size, size), 0)
    ImageDraw.Draw(glow).ellipse(
        (int(size * 0.10), int(size * 0.04), int(size * 0.90), int(size * 0.84)),
        fill=90,
    )
    glow = glow.filter(ImageFilter.GaussianBlur(size * 0.13))

    shape = Image.new("L", (size, size), 0)
    ImageDraw.Draw(shape).rounded_rectangle(
        (0, 0, size - 1, size - 1), radius=int(size * 0.20), fill=255
    )

    tile = Image.new("RGBA", (size, size), BG + (255,))
    tint = Image.new("RGBA", (size, size), (44, 120, 255, 255))
    tint.putalpha(glow.point(lambda v: int(v * 0.5)))
    tile.alpha_composite(tint)

    card = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    card.paste(tile, (0, 0), shape)
    return card


def load_font(size: int):
    for name in ("segoeuib.ttf", "segoeui.ttf", "arialbd.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def main() -> None:
    ASSETS.mkdir(exist_ok=True)
    INSTALLER.mkdir(exist_ok=True)

    src = Image.open(SRC).convert("RGB")
    logo = emblem(src)
    print(f"logo cutout: {logo.width}x{logo.height}")

    # Full transparent logo (for the app / About screen).
    square(logo, 1024, 0.04).save(ASSETS / "logo.png")

    # App + installer icon: logo on a dark rounded tile.
    card = rounded_card(1024)
    card.alpha_composite(square(logo, 1024, 0.13))
    card.save(ASSETS / "icon.png")

    sizes = [16, 24, 32, 48, 64, 128, 256]
    card.save(ASSETS / "icon.ico", sizes=[(s, s) for s in sizes])
    print("wrote assets/icon.png, assets/icon.ico, assets/logo.png")

    # ---- Inno Setup wizard artwork -------------------------------------
    w, h = 164, 314
    large = Image.new("RGB", (w, h), BG)
    glow = Image.new("L", (w, h), 0)
    ImageDraw.Draw(glow).ellipse((-50, 0, w + 50, 260), fill=80)
    glow = glow.filter(ImageFilter.GaussianBlur(32))
    large.paste(Image.new("RGB", (w, h), (36, 96, 190)), (0, 0), glow)

    fit = square(logo, 250, 0.02)
    large.paste(fit, ((w - 250) // 2, 22), fit)

    draw = ImageDraw.Draw(large)
    font = load_font(15)
    text = "SwiftBase"
    box = draw.textbbox((0, 0), text, font=font)
    draw.text(((w - (box[2] - box[0])) / 2, 285), text, font=font, fill=(233, 239, 248))
    large.save(INSTALLER / "wizard-large.bmp")

    small = Image.new("RGB", (55, 55), BG)
    sm = square(logo, 51, 0.02)
    small.paste(sm, (2, 2), sm)
    small.save(INSTALLER / "wizard-small.bmp")
    print("wrote installer/wizard-large.bmp, installer/wizard-small.bmp")


if __name__ == "__main__":
    main()
