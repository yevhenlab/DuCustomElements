"""Small deterministic mesh thumbnail renderer; no client screenshots or external assets required."""
import math
import struct
import zlib

import numpy as np


def png_chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))


def render_icon(triangles, materials, size=256):
    vertices = np.array([points for _, points in triangles], dtype=float)
    # Isometric view with world Z up. Positive depth faces the camera.
    basis = np.array([[1 / math.sqrt(2), 1 / math.sqrt(2), 0],
                      [-0.35, 0.35, 0.86890736], [0.6144103, -0.6144103, 0.495]])
    projected = vertices @ basis.T
    lower, upper = projected[:, :, :2].min(axis=(0, 1)), projected[:, :, :2].max(axis=(0, 1))
    scale = (size - 32) / max(upper - lower)
    projected[:, :, :2] = (projected[:, :, :2] - (lower + upper) / 2) * scale + size / 2
    projected[:, :, 1] = size - projected[:, :, 1]
    pixels = np.zeros((size, size, 4), dtype=np.uint8)
    depths = np.full((size, size), -np.inf)
    for index, (identity, points) in enumerate(triangles):
        raster_triangle(pixels, depths, projected[index], points, materials[identity])
    raw = b''.join(b'\x00' + row.tobytes() for row in pixels)
    return (b'\x89PNG\r\n\x1a\n' + png_chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)) +
            png_chunk(b'IDAT', zlib.compress(raw, 9)) + png_chunk(b'IEND', b''))


def raster_triangle(pixels, depths, screen, world, material):
    height, width = depths.shape
    lower = np.maximum(0, np.floor(screen[:, :2].min(axis=0)).astype(int))
    upper = np.minimum([width - 1, height - 1], np.ceil(screen[:, :2].max(axis=0)).astype(int))
    if np.any(lower > upper):
        return
    a, b, c = screen
    determinant = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
    if abs(determinant) < 1e-10:
        return
    y, x = np.mgrid[lower[1]:upper[1] + 1, lower[0]:upper[0] + 1] + 0.5
    w0 = ((b[1] - c[1]) * (x - c[0]) + (c[0] - b[0]) * (y - c[1])) / determinant
    w1 = ((c[1] - a[1]) * (x - c[0]) + (a[0] - c[0]) * (y - c[1])) / determinant
    w2 = 1 - w0 - w1
    depth = w0 * a[2] + w1 * b[2] + w2 * c[2]
    target = depths[lower[1]:upper[1] + 1, lower[0]:upper[0] + 1]
    visible = (w0 >= 0) & (w1 >= 0) & (w2 >= 0) & (depth > target)
    normal = np.cross(np.subtract(world[1], world[0]), np.subtract(world[2], world[0]))
    normal /= max(np.linalg.norm(normal), 1e-12)
    light = 0.35 + 0.65 * abs(float(normal @ np.array([0.3, -0.5, 0.81240384])))
    color = np.clip(np.array(material.get('albedo', [0.8, 0.8, 0.8]))[:3] * light * 255, 0, 255)
    pixels[lower[1]:upper[1] + 1, lower[0]:upper[0] + 1][visible] = [*color.astype(int), 255]
    target[visible] = depth[visible]
