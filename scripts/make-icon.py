#!/usr/bin/env python3
"""Generate src/BatteryChargeMeter.ico — solid Fluent squircle + white battery-bolt.

Renders each icon size with size-adaptive stroke widths (supersampled 8x),
then packs a PNG-compressed multi-size ICO by hand.
"""
import io, struct
from PIL import Image, ImageDraw

EMERALD = (5, 150, 105, 255)
WHITE = (255, 255, 255, 255)
SIZES = (16, 20, 24, 32, 40, 48, 64, 256)
STROKE_GRID = {16: 2.7, 20: 2.5, 24: 2.3}  # heavier relative strokes below 32


def stroke_grid(size):
    return STROKE_GRID.get(size, 2.0 if size >= 48 else 2.1)


def render(size):
    ss = 8
    img = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, size * ss - 1, size * ss - 1],
                        radius=size * 0.24 * ss, fill=EMERALD)
    g = size / 24.0 * ss
    sw = max(1, round(stroke_grid(size) * g))

    def x(v): return v * g
    # Lucide battery-charging body: two split paths, gaps where the bolt passes.
    # Left:  M6 7H4 a2 2 0 0 0-2 2 v6 a2 2 0 0 0 2 2 h1
    d.line([x(6), x(7), x(4), x(7)], fill=WHITE, width=sw)
    d.arc([x(2), x(7), x(6), x(11)], 180, 270, fill=WHITE, width=sw)
    d.line([x(2), x(9), x(2), x(15)], fill=WHITE, width=sw)
    d.arc([x(2), x(13), x(6), x(17)], 90, 180, fill=WHITE, width=sw)
    d.line([x(4), x(17), x(5), x(17)], fill=WHITE, width=sw)
    # Right: M15 7h1 a2 2 0 0 1 2 2 v6 a2 2 0 0 1-2 2 h-2
    d.line([x(15), x(7), x(16), x(7)], fill=WHITE, width=sw)
    d.arc([x(14), x(7), x(18), x(11)], 270, 360, fill=WHITE, width=sw)
    d.line([x(18), x(9), x(18), x(15)], fill=WHITE, width=sw)
    d.arc([x(14), x(13), x(18), x(17)], 0, 90, fill=WHITE, width=sw)
    d.line([x(16), x(17), x(14), x(17)], fill=WHITE, width=sw)
    # terminal nub
    d.line([x(22), x(11), x(22), x(13)], fill=WHITE, width=sw)
    # bolt: m11 7 -3 5 h4 l-3 5 — stroked at large sizes, filled below 32px
    if size <= 24:
        d.polygon([(x(11.6), x(6.2)), (x(7.4), x(12.8)), (x(10.9), x(12.8)),
                   (x(8.6), x(17.8)), (x(13.2), x(11.2)), (x(9.7), x(11.2))],
                  fill=WHITE)
    else:
        bolt = [(11, 7), (8, 12), (12, 12), (9, 17)]
        d.line([(x(a), x(b)) for a, b in bolt], fill=WHITE, width=sw, joint="curve")
        for pt in (bolt[0], bolt[-1]):  # round caps on open ends
            cx, cy = x(pt[0]), x(pt[1])
            d.ellipse([cx - sw / 2, cy - sw / 2, cx + sw / 2, cy + sw / 2],
                      fill=WHITE)
    return img.resize((size, size), Image.LANCZOS)


def png_bytes(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def pack_ico(entries):
    header = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    directory, payload = b"", b""
    for size, png in entries:
        directory += struct.pack("<BBBBHHII", size if size < 256 else 0,
                                 size if size < 256 else 0, 0, 0, 1, 32,
                                 len(png), offset)
        payload += png
        offset += len(png)
    return header + directory + payload


def main():
    images = {size: render(size) for size in SIZES}
    ico = pack_ico([(size, png_bytes(img)) for size, img in images.items()])
    out = "src/BatteryChargeMeter.ico"
    with open(out, "wb") as fh:
        fh.write(ico)
    print(f"wrote {out} ({len(ico)} bytes, sizes {SIZES})")

    # contact sheet for review
    pad, bg = 12, (240, 241, 243, 255)
    width = sum(SIZES) + pad * (len(SIZES) + 1)
    height = 256 + 2 * pad
    sheet = Image.new("RGBA", (width, height), bg)
    x = pad
    for size in SIZES:
        sheet.paste(images[size], (x, pad + (256 - size) // 2), images[size])
        x += size + pad
    sheet.save("/tmp/bcm-icon-sheet.png")
    print("wrote /tmp/bcm-icon-sheet.png")


if __name__ == "__main__":
    main()
