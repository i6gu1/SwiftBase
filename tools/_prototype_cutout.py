from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
src = Image.open(ROOT / "icon.jpg").convert("RGB")
rgb = np.asarray(src)
lum = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114

mask = Image.fromarray(((lum > 52) * 255).astype(np.uint8), "L")
mask = mask.filter(ImageFilter.MaxFilter(7)).filter(ImageFilter.MinFilter(7))
m = np.asarray(mask) > 0

h, w = m.shape
inv = ~m
visited = np.zeros_like(inv)
stack = []
for x in range(w):
    for y in (0, h - 1):
        if inv[y, x] and not visited[y, x]:
            visited[y, x] = True
            stack.append((y, x))
for y in range(h):
    for x in (0, w - 1):
        if inv[y, x] and not visited[y, x]:
            visited[y, x] = True
            stack.append((y, x))
while stack:
    y, x = stack.pop()
    for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
        if 0 <= ny < h and 0 <= nx < w and inv[ny, nx] and not visited[ny, nx]:
            visited[ny, nx] = True
            stack.append((ny, nx))
filled = m | (~visited)
print("bbox", Image.fromarray((filled * 255).astype(np.uint8)).getbbox())

alpha = Image.fromarray((filled * 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(1.2))
out = src.convert("RGBA")
out.putalpha(alpha)
bbox = alpha.getbbox()
out = out.crop(bbox)
print("crop", out.size)

# preview on magenta to show remaining background
prev = Image.new("RGB", out.size, (255, 0, 255))
prev.paste(out, (0, 0), out)
prev.thumbnail((700, 700))
prev.save(ROOT / "assets" / "_preview.png")
