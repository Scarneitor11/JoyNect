#!/usr/bin/env python3
"""Draws JoyNect's application icon.

The mark is a bridge: two posts and an arch, which is what the app does (it carries a Switch
controller's input across to an Xbox pad). Drawn here rather than exported from a design tool so the
icon is reproducible, has no binary source of truth, and can be regenerated at any size.

Each size is rendered with 4x supersampling so the arch stays smooth at 16 px.
"""
import math
import struct
import zlib
from pathlib import Path

SIZES = (16, 24, 32, 48, 64, 128, 256)
SUPERSAMPLE = 4

TOP = (0x8A, 0x74, 0xD8)
BOTTOM = (0x4F, 0x37, 0x8B)


def rounded_rect_alpha(x, y, left, top, right, bottom, radius):
    """Coverage of a rounded rectangle at a point, 0..1."""
    cx = min(max(x, left + radius), right - radius)
    cy = min(max(y, top + radius), bottom - radius)
    d = math.hypot(x - cx, y - cy)
    return 1.0 if d <= radius else 0.0


def capsule_distance(px, py, ax, ay, bx, by):
    abx, aby = bx - ax, by - ay
    length2 = abx * abx + aby * aby
    t = 0.0 if length2 == 0 else max(0.0, min(1.0, ((px - ax) * abx + (py - ay) * aby) / length2))
    return math.hypot(px - (ax + t * abx), py - (ay + t * aby))


def body_distance(x, y, size):
    """Signed distance to the gamepad silhouette (negative inside)."""
    s = size

    # The shell: a wide pill for the body, two round grips at its lower corners.
    body = rounded_rect_signed(x, y, 0.235 * s, 0.395 * s, 0.765 * s, 0.575 * s, 0.085 * s)
    left_grip = math.hypot(x - 0.30 * s, y - 0.585 * s) - 0.092 * s
    right_grip = math.hypot(x - 0.70 * s, y - 0.585 * s) - 0.092 * s
    return min(body, left_grip, right_grip)


def rounded_rect_signed(x, y, left, top, right, bottom, radius):
    cx = min(max(x, left + radius), right - radius)
    cy = min(max(y, top + radius), bottom - radius)
    return math.hypot(x - cx, y - cy) - radius


def is_detail(x, y, size):
    """The cut-outs: a d-pad cross on the left, a four button cluster on the right."""
    s = size
    cx, cy = 0.355 * s, 0.485 * s
    arm, half = 0.052 * s, 0.015 * s

    # A cross is two overlapping bars.
    if abs(x - cx) <= arm and abs(y - cy) <= half:
        return True
    if abs(x - cx) <= half and abs(y - cy) <= arm:
        return True

    bx, by = 0.665 * s, 0.485 * s
    offset, radius = 0.036 * s, 0.0155 * s
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        if math.hypot(x - (bx + dx * offset), y - (by + dy * offset)) <= radius:
            return True

    return False


def render(size):
    hi = size * SUPERSAMPLE
    pixels = bytearray()
    inset = 0.055 * hi
    radius = 0.225 * hi

    for py in range(size):
        for px in range(size):
            r = g = b = a = 0.0
            for sy in range(SUPERSAMPLE):
                for sx in range(SUPERSAMPLE):
                    x = px * SUPERSAMPLE + sx + 0.5
                    y = py * SUPERSAMPLE + sy + 0.5

                    if rounded_rect_alpha(x, y, inset, inset, hi - inset, hi - inset, radius) == 0.0:
                        continue

                    t = y / hi
                    bg = (
                        TOP[0] + (BOTTOM[0] - TOP[0]) * t,
                        TOP[1] + (BOTTOM[1] - TOP[1]) * t,
                        TOP[2] + (BOTTOM[2] - TOP[2]) * t,
                    )

                    coverage = 0.0
                    if body_distance(x, y, hi) <= 0 and not is_detail(x, y, hi):
                        coverage = 1.0

                    r += bg[0] + (255 - bg[0]) * coverage
                    g += bg[1] + (255 - bg[1]) * coverage
                    b += bg[2] + (255 - bg[2]) * coverage
                    a += 255.0

            samples = SUPERSAMPLE * SUPERSAMPLE
            if a == 0:
                pixels.extend((0, 0, 0, 0))
            else:
                pixels.extend((
                    int(min(255, r / samples)),
                    int(min(255, g / samples)),
                    int(min(255, b / samples)),
                    int(min(255, a / samples)),
                ))

    return bytes(pixels)


def png(width, height, rgba):
    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)
        raw.extend(rgba[y * stride:(y + 1) * stride])

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        + chunk(b"IEND", b"")
    )


def main():
    root = Path(__file__).resolve().parent.parent
    assets = root / "src" / "JoyNect.App" / "assets"
    assets.mkdir(parents=True, exist_ok=True)

    images = []
    for size in SIZES:
        data = png(size, size, render(size))
        images.append((size, data))
        (assets / f"icon-{size}.png").write_bytes(data)

    directory = bytearray()
    directory += struct.pack("<HHH", 0, 1, len(images))

    offset = 6 + 16 * len(images)
    payload = bytearray()
    for size, data in images:
        # PNG-compressed entries: a zero plane/bit count is the modern convention.
        directory += struct.pack("<BBBBHHII", size if size < 256 else 0, size if size < 256 else 0, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        payload += data

    ico = bytes(directory + payload)
    (assets / "app.ico").write_bytes(ico)

    # Keep the largest PNG alongside for README and store listings.
    (assets / "app.png").write_bytes(images[-1][1])
    print(f"wrote app.ico ({len(ico)} bytes) and icon sizes {SIZES}")


if __name__ == "__main__":
    main()
