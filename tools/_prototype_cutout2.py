from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
src = Image.open(ROOT / "icon.jpg").convert("RGB")
rgb = np.asarray(src)
r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
h, w = r.shape

candidate = (r < 72) & (g < 88) & (b < 110) & ((b.astype(np.int16) - r.astype(np.int16)) < 82)
print("candidate px", int(candidate.sum()))

visited = np.zeros((h, w), dtype=bool)
stack = []


def seed(y, x):
    if candidate[y, x] and not visited[y, x]:
        visited[y, x] = True
        stack.append((y, x))


for x in range(w):
    seed(0, x)
    seed(h - 1, x)
for y in range(h):
    seed(y, 0)
    seed(y, w - 1)

while stack:
    y, x = stack.pop()
    if y > 0 and candidate[y - 1, x] and not visited[y - 1, x]:
        visited[y - 1, x] = True
        stack.append((y - 1, x))
    if y + 1 < h and candidate[y + 1, x] and not visited[y + 1, x]:
        visited[y + 1, x] = True
        stack.append((y + 1, x))
    if x > 0 and candidate[y, x - 1] and not visited[y, x - 1]:
        visited[y, x - 1] = True
        stack.append((y, x - 1))
    if x + 1 < w and candidate[y, x + 1] and not visited[y, x + 1]:
        visited[y, x + 1] = True
        stack.append((y, x + 1))

alpha = np.where(visited, 0, 255).astype(np.uint8)
img = Image.fromarray(alpha, "L").filter(ImageFilter.GaussianBlur(0.8))
img = img.point(lambda v: 0 if v < 40 else min(255, int(v * 1.4)))
print("bbox", img.getbbox())

out = src.convert("RGBA")
out.putalpha(img)
prev = Image.new("RGB", out.size, (255, 0, 255))
prev.paste(out, (0, 0), out)
prev.thumbnail((700, 700))
prev.save(ROOT / "assets" / "_preview2.png")
print("done")
