#!/usr/bin/env python3
"""Generate the JoyNect application icon.

Draws a Material 3 flavoured app icon (tonal squircle + white controller glyph) at high
resolution and lets Pillow downsample it into a multi-size Windows .ico, so the small sizes
stay clean instead of turning to mush.

Usage:  python tools/gen_app_icon.py [output.ico]
"""

from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw

# M3 baseline-ish primary/tertiary pair, matching the app's default seed.
PRIMARY = (103, 80, 164)
TERTIARY = (125, 82, 96)
SURFACE_FACE = (255, 255, 255)

SS = 1024  # supersample canvas
ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))  # type: ignore[return-value]


def diagonal_gradient(size: int, start: tuple[int, int, int], end: tuple[int, int, int]) -> Image.Image:
    """Cheap diagonal gradient: draw 2*size thin lines, then rotate 45 degrees."""
    ramp = Image.new("RGB", (size, size))
    d = ImageDraw.Draw(ramp)
    for i in range(size * 2):
        d.line([(i, 0), (i - size, size)], fill=lerp(start, end, i / (size * 2)), width=2)
    return ramp


def rounded_mask(size: int, radius: int) -> Image.Image:
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return m


def build_master() -> Image.Image:
    """Full-bleed squircle with a bold controller glyph."""
    bg = diagonal_gradient(SS, PRIMARY, TERTIARY)
    icon = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))
    icon.paste(bg, (0, 0), rounded_mask(SS, int(SS * 0.225)))

    # The glyph is drawn on its own layer so it can be inset consistently.
    glyph = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))
    g = ImageDraw.Draw(glyph)

    # Controller body: two grips + a centre bar (union of primitives keeps it tame to read).
    y = int(SS * 0.5)
    grip_r = int(SS * 0.135)
    left_c = (int(SS * 0.315), y)
    right_c = (int(SS * 0.685), y)
    for c in (left_c, right_c):
        g.ellipse(
            [c[0] - grip_r, c[1] - grip_r, c[0] + grip_r, c[1] + grip_r],
            fill=SURFACE_FACE,
        )
    bar = [left_c[0], y - int(SS * 0.125), right_c[0], y + int(SS * 0.125)]
    g.rounded_rectangle(bar, radius=int(SS * 0.125), fill=SURFACE_FACE)

    # D-pad cross (left) and a two-button cluster (right), punched in the background tone.
    notch = Image.new("RGBA", (SS, SS), (0, 0, 0, 0))
    n = ImageDraw.Draw(notch)
    dpad_cx, dpad_cy = left_c[0], y
    arm, thick = int(SS * 0.062), int(SS * 0.028)
    n.rounded_rectangle(
        [dpad_cx - arm, dpad_cy - thick, dpad_cx + arm, dpad_cy + thick],
        radius=thick,
        fill=SURFACE_FACE,
    )
    n.rounded_rectangle(
        [dpad_cx - thick, dpad_cy - arm, dpad_cx + thick, dpad_cy + arm],
        radius=thick,
        fill=SURFACE_FACE,
    )
    btn_r = int(SS * 0.030)
    for dx, dy in ((int(SS * 0.062), -int(SS * 0.052)), (int(SS * 0.020), int(SS * 0.032))):
        bx, by = right_c[0] - int(SS * 0.04) + dx, y + dy
        n.ellipse([bx - btn_r, by - btn_r, bx + btn_r, by + btn_r], fill=SURFACE_FACE)

    # Subtract the notch shapes from the glyph, then stamp the glyph onto the squircle.
    glyph_alpha = glyph.split()[3]
    notch_alpha = notch.split()[3]
    glyph_alpha = Image.eval(
        Image.merge("L", (glyph_alpha,)).convert("L"),
        lambda v: v,
    )
    cut = Image.new("L", (SS, SS), 0)
    cut.paste(255, (0, 0), glyph_alpha)
    cut.paste(0, (0, 0), notch_alpha)
    glyph.putalpha(cut)

    icon.alpha_composite(glyph)
    return icon


def main() -> int:
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "src/JoyNect.App/assets/app.ico")
    out.parent.mkdir(parents=True, exist_ok=True)
    master = build_master()
    master.save(out, format="ICO", sizes=ICO_SIZES)
    print(f"wrote {out} ({out.stat().st_size} bytes, sizes: {', '.join(f'{w}x{h}' for w, h in ICO_SIZES)})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
