#!/usr/bin/env python3
"""Generate ConveyorBelts placeholder textures (stdlib only). Run: python3 -I tools/make_textures.py"""
import os, struct, zlib

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Textures", "ConveyorBelts")
N = 64

def png(path, px):
    raw = b"".join(b"\x00" + bytes(v for p in px[y * N:(y + 1) * N] for v in p) for y in range(N))
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", N, N, 8, 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)
    print(os.path.normpath(path))

def hexrgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))

def in_round_rect(x, y, m=3, r=10):
    lo, hi = m, N - 1 - m
    if x < lo or x > hi or y < lo or y > hi:
        return False
    cx = min(max(x, lo + r), hi - r)
    cy = min(max(y, lo + r), hi - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r

def in_arrow_up(x, y):
    # head: triangle apex (32,8) to base y=30 spanning x 14..50; shaft x 26..38, y 30..56
    if 8 <= y <= 30:
        half = (y - 8) / 22 * 18
        return abs(x - 31.5) <= half
    return 30 < y <= 56 and 26 <= x <= 37

def rot(x, y, d):
    """Map output pixel to source (up-arrow frame) pixel for direction d: 0 N,1 E,2 S,3 W."""
    for _ in range(d):  # rotate 90 degrees clockwise: source = (y, N-1-x)
        x, y = y, N - 1 - x
    return x, y

DIRS = ["north", "east", "south", "west"]
WHITE = (255, 255, 255, 255)

def sprite(color, d, border):
    base = hexrgb(color)
    dark = tuple(int(c * 0.55) for c in base)
    px = []
    for y in range(N):
        for x in range(N):
            if not in_round_rect(x, y):
                px.append((0, 0, 0, 0)); continue
            sx, sy = rot(x, y, d)
            if in_arrow_up(sx, sy):
                px.append(WHITE); continue
            c = base
            if border:
                # border on the two sides parallel to travel (left/right of the arrow): source-frame x edges
                if sx < 3 + 4 or sx > N - 4 - 5:
                    c = dark
            px.append(c + (255,))
    return px

def icon_px(fn):
    return [(255, 255, 255, 255) if fn(x, y) else (0, 0, 0, 0) for y in range(N) for x in range(N)]

def seg(x, y, x0, y0, x1, y1, w):
    dx, dy = x1 - x0, y1 - y0
    t = max(0, min(1, ((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy)))
    return (x - (x0 + t * dx)) ** 2 + (y - (y0 + t * dy)) ** 2 <= w * w

ICONS = {
    # a line of three dots joined by a bar
    "SelectLine": lambda x, y: seg(x, y, 10, 32, 54, 32, 3) or any((x - c) ** 2 + (y - 32) ** 2 <= 49 for c in (12, 32, 52)),
    # circular arrow (ring with a gap plus arrowhead)
    "RotateMotor": lambda x, y: (200 <= (x - 32) ** 2 + (y - 32) ** 2 <= 380 and not (x > 32 and y < 30)) or
                                 (x > 32 and y < 30 and in_tri(x, y)),
    # tab: rounded outline of a tab shape
    "TabPort": lambda x, y: (seg(x, y, 10, 50, 10, 18, 2.5) or seg(x, y, 10, 18, 26, 18, 2.5) or seg(x, y, 26, 18, 32, 26, 2.5)
                             or seg(x, y, 32, 26, 54, 26, 2.5) or seg(x, y, 54, 26, 54, 50, 2.5) or seg(x, y, 10, 50, 54, 50, 2.5)),
}

def in_tri(x, y):
    return 34 <= x <= 52 and 6 <= y <= 24 and (x - 34) + (24 - y) >= 6 and (x - 34) >= (y - 6) * 0.0

def main():
    entries = [("Belt_Basic", "#E8C33A", True), ("Belt_Fast", "#D9483B", True), ("Belt_Express", "#3B7DD9", True),
               ("Motor", "#8A8F96", False), ("InputPort", "#3FAF5A", False),
               ("PusherPort", "#E58A2E", False), ("OutputPort", "#2EC4C9", False)]
    for name, color, border in entries:
        for i, dn in enumerate(DIRS):
            png(os.path.join(ROOT, "%s_%s.png" % (name, dn)), sprite(color, i, border))
    for name, fn in ICONS.items():
        png(os.path.join(ROOT, "UI", name + ".png"), icon_px(fn))

main()
