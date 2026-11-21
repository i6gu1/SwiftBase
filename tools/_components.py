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

# fill holes
inv = ~m
visited = np.zeros_like(inv)
stack = []
for x in range(w):
    for y in (0, h - 1):
        if inv[y, x]:
            visited[y, x] = True
            stack.append((y, x))
for y in range(h):
    for x in (0, w - 1):
        if inv[y, x]:
            visited[y, x] = True
            stack.append((y, x))
while stack:
    y, x = stack.pop()
    for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
        if 0 <= ny < h and 0 <= nx < w and inv[ny, nx] and not visited[ny, nx]:
            visited[ny, nx] = True
            stack.append((ny, nx))
filled = m | (~visited)
print("filled px", int(filled.sum()))

# connected components of filled mask
labels = np.zeros((h, w), dtype=np.int32)
cur = 0
comps = []
stack = []
for yy in range(h):
    for xx in range(w):
        if not filled[yy, xx] or labels[yy, xx]:
            continue
        cur += 1
        labels[yy, xx] = cur
        stack.append((yy, xx))
        area = 0
        minx = maxx = xx
        miny = maxy = yy
        while stack:
            y, x = stack.pop()
            area += 1
            minx = min(minx, x); maxx = max(maxx, x)
            miny = min(miny, y); maxy = max(maxy, y)
            for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
                if 0 <= ny < h and 0 <= nx < w and filled[ny, nx] and not labels[ny, nx]:
                    labels[ny, nx] = cur
                    stack.append((ny, nx))
        comps.append((area, (minx, miny, maxx, maxy)))

comps.sort(reverse=True)
print("top components:")
for area, box in comps[:12]:
    print(area, box, "size", box[2] - box[0] + 1, "x", box[3] - box[1] + 1)
