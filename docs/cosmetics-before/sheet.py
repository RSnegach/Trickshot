"""Combine the per-view gallery PNGs into one labelled contact sheet per item.
usage: python sheet.py <gallery_dir> <out_dir> [size]
"""
import os, sys, re
from collections import defaultdict
from PIL import Image, ImageDraw

src, dst = sys.argv[1], sys.argv[2]
size = int(sys.argv[3]) if len(sys.argv) > 3 else 400
os.makedirs(dst, exist_ok=True)
order = ["front", "q34", "side", "back", "body", "head", "rear"]
items = defaultdict(dict)
for f in os.listdir(src):
    if not f.endswith(".png"):
        continue
    m = re.match(r"(.+)_(front|q34|side|back|body|head|rear)\.png$", f)
    if not m:
        continue
    items[m.group(1)][m.group(2)] = os.path.join(src, f)

for item, views in sorted(items.items()):
    keys = [k for k in order if k in views]
    w = size * len(keys)
    sheet = Image.new("RGB", (w, size + 28), (20, 20, 24))
    d = ImageDraw.Draw(sheet)
    d.text((6, 6), item, fill=(240, 240, 240))
    for i, k in enumerate(keys):
        im = Image.open(views[k]).convert("RGB").resize((size, size), Image.LANCZOS)
        sheet.paste(im, (i * size, 28))
        d.text((i * size + 6, 30), k, fill=(255, 230, 120))
    sheet.save(os.path.join(dst, item + ".png"))
print(f"{len(items)} sheets")
