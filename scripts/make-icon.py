#!/usr/bin/env python3
"""Render the Lucide battery-medium artwork with CairoSVG and Pillow.

Run: uv run --with cairosvg --with pillow python scripts/make-icon.py
The SVG is the editable source; all Windows icon sizes are packed by Pillow.
"""
import io
from pathlib import Path
import tempfile

import cairosvg
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def main():
    png = cairosvg.svg2png(url=str(ROOT / "src/BatteryChargeMeter.svg"),
                          output_width=1024, output_height=1024)
    source = Image.open(io.BytesIO(png)).convert("RGBA")
    source.save(ROOT / "src/BatteryChargeMeter.ico", sizes=[(n, n) for n in SIZES])
    sheet = Image.new("RGBA", (sum(SIZES) + 16 * (len(SIZES) + 1), 288), "#f3f4f5")
    x = 16
    for size in SIZES:
        icon = source.resize((size, size), Image.Resampling.LANCZOS)
        sheet.alpha_composite(icon, (x, (288 - size) // 2))
        x += size + 16
    preview = Path(tempfile.gettempdir()) / "bcm-icon-sheet.png"
    sheet.save(preview)
    print(f"Generated BatteryChargeMeter.ico ({SIZES}); preview: {preview}")


if __name__ == "__main__":
    main()
