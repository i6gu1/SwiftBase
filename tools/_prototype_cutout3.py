from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
src = Image.open(ROOT / "icon.jpg").convert("RGB")
rgb = np.array(src).astype(np.int16)
r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
lum = r * 0.299 + g * 0.587 + b * 0.114

bg = ((b - 1.5 * g) < 5) & (lum < 65)
print("bg frac", round(float(bg.mean()), 3))

fg = Image.fromarray(((~bg) * 255).astype(np.uint8), "L")
fg = fg.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
fg = fg.filter(ImageFilter.MaxFilter(7)).filter(ImageFilter.MinFilter(7))
m = np.asarray(fg) > 0

h, w = m.shape
# fill holes
inv = ~m
seen = np.zeros((h, w), bool)
stack = []
for x in range(w):
    for y in (0, h - 1):
        if inv[y, x] and not seen[y, x]:
            seen[y, x] = True; stack.append((y, x))
for y in range(h):
    for x in (0, w - 1):
        if inv[y, x] and not seen[y, x]:
            seen[y, x] = True; stack.append((y, x))
while stack:
    y, x = stack.pop()
    for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
        if 0 <= ny < h and 0 <= nx < w and inv[ny, nx] and not seen[ny, nx]:
            seen[ny, nx] = True; stack.append((ny, nx))
solid = m | (~seen)

# largest component
seen = np.zeros((h, w), bool)
best = 0
bestp = []
for y0 in range(h):
    for x0 in range(w):
        if not solid[y0, x0] or seen[y0, x0]:
            continue
        seen[y0, x0] = True
        st = [(y0, x0)]
        pts = []
        while st:
            y, x = st.pop()
            pts.append((y, x))
            for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                if 0 <= ny < h and 0 <= nx < w and solid[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    st.append((ny, nx))
        if len(pts) > best:
            best = len(pts); bestp = pts

mask = np.zeros((h, w), np.uint8)
ys = np.array([p[0] for p in bestp]); xs = np.array([p[1] for p in bestp])
mask[ys, xs] = 255
alpha = Image.fromarray(mask, "L").filter(ImageFilter.GaussianBlur(0.8)).point(
    lambda v: 0 if v < 35 else min(255, int(v * 1.35)))
bbox = alpha.getbbox()
print("bbox", bbox, "size", (bbox[2] - bbox[0], bbox[3] - bbox[1]))

alpha = alpha.crop(bbox)
repaint = bg & (np.asarray(alpha.resize((w, h))) > 0) if alpha.size == (w, h) else None
out = Image.fromarray(rgb.astype(np.uint8), "RGB").convert("RGBA").crop(bbox)
out.putalpha(alpha)
prev = Image.new("RGB", out.size, (255, 0, 255))
prev.paste(out, (0, 0), out)
prev.thumbnail((700, 700))
prev.save(ROOT / "assets" / "_preview3.png")
print("done")
