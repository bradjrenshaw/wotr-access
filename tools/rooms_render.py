"""Render a RoomMap dump as a colored floor plan PNG — the authoring eye for the room-curation
pipeline (docs: coordinate-anchored curation in assets/descriptions/<Area>.json).

Usage:
    python tools/rooms_render.py <dump.txt> [out.png] [--curation assets/descriptions/Area.json]

The dump comes from the LIVE game (never offline scene extraction):
    curl -X POST --data-binary 'WrathAccess.Dev.DevSurvey.DumpRooms(null)' http://127.0.0.1:8771/eval
    (via reflection when internal: see the dev-server notes)

Output: rooms in stable colors with their ids at the centroid, unwalkable near-black, exits as
white dots, and — with --curation — virtual walls in red, merge groups in yellow (anchors linked),
title anchors as cyan rings."""
import json
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    dump_path = args[0]
    out_path = args[1] if len(args) > 1 else dump_path.rsplit(".", 1)[0] + ".png"
    curation = None
    if "--curation" in sys.argv:
        cur_path = sys.argv[sys.argv.index("--curation") + 1]
        curation = json.load(open(cur_path, encoding="utf-8"))

    lines = open(dump_path, encoding="utf-8").read().split("\n")
    head = lines[0].split()
    w, h, cell, x0, z0 = int(head[0]), int(head[1]), float(head[2]), float(head[3]), float(head[4])
    area = head[5] if len(head) > 5 else "?"
    label = np.array([int(x) for x in lines[1].split(",") if x != ""], dtype=np.int32).reshape(h, w)
    rooms, exits = [], []
    for line in lines[3:]:
        if line.startswith("R "):
            rid, cls, sqm, cx, cy, cz = line[2:].split("|")
            rooms.append((int(rid), cls, float(sqm), float(cx), float(cy), float(cz)))
        elif line.startswith("E "):
            fr, to, x, y, z = line[2:].split("|")
            exits.append((int(fr), int(to), float(x), float(y), float(z)))

    SCALE = 3  # pixels per cell

    def px(wx, wz):  # world -> image, north (+z) UP: flip the row axis here, not at save time
        return int((wx - x0) / cell) * SCALE, (h - 1 - int((wz - z0) / cell)) * SCALE

    rng = np.random.RandomState(42)
    palette = rng.randint(70, 240, (max(label.max() + 2, 2), 3))
    img = np.zeros((h, w, 3), np.uint8)
    img[label < 0] = (24, 24, 24)
    m = label >= 0
    img[m] = palette[label[m]]
    img = img[::-1]  # north up
    im = Image.fromarray(np.repeat(np.repeat(img, SCALE, 0), SCALE, 1))
    draw = ImageDraw.Draw(im)
    try:
        font = ImageFont.truetype("arial.ttf", 8 * SCALE)
    except OSError:
        font = ImageFont.load_default()

    for fr, to, x, y, z in exits:
        cx, cz = px(x, z)
        draw.ellipse([cx - SCALE, cz - SCALE, cx + SCALE, cz + SCALE], fill=(255, 255, 255))
    for rid, cls, sqm, cx, cy, cz in rooms:
        ix, iz = px(cx, cz)
        text = f"{rid}"
        draw.text((ix + 1, iz + 1), text, fill=(0, 0, 0), font=font, anchor="mm")
        draw.text((ix, iz), text, fill=(255, 255, 255), font=font, anchor="mm")

    if curation:
        for wall in curation.get("walls") or []:
            pts = [px(p[0], p[1]) for p in (wall.get("points") or [])]
            if len(pts) >= 2:
                draw.line(pts, fill=(255, 40, 40), width=SCALE)
        for group in curation.get("merges") or []:
            pts = [px(a[0], a[2]) for a in group if len(a) >= 3]
            if len(pts) >= 2:
                draw.line(pts, fill=(255, 220, 40), width=1)
            for ix, iz in pts:
                draw.ellipse([ix - 2 * SCALE, iz - 2 * SCALE, ix + 2 * SCALE, iz + 2 * SCALE],
                             outline=(255, 220, 40), width=2)
        for r in curation.get("rooms") or []:
            ix, iz = px(r["x"], r["z"])
            draw.ellipse([ix - 2 * SCALE, iz - 2 * SCALE, ix + 2 * SCALE, iz + 2 * SCALE],
                         outline=(40, 230, 230), width=2)

    im.save(out_path)
    print(f"{area}: {len(rooms)} rooms -> {out_path} ({im.width}x{im.height})")


if __name__ == "__main__":
    main()
