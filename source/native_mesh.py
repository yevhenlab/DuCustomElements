"""Unigine ms11 static meshes, compatible with the installed DU client.

Layout: https://developer.unigine.com/ru/docs/2.17/code/formats/file_formats
Compact integers and the layout are also checked against Novaquark's CustomButtonAFixed.mesh.
"""
import math
import struct


def compact(value):
    if not 0 <= value <= 0x7fffffff:
        raise ValueError('Mesh count is outside the supported positive integer range')
    first, value = value & 63, value >> 6
    result = bytearray([first | (128 if value else 0)])
    while value:
        part, value = value & 127, value >> 7
        result.append(part | (128 if value else 0))
    return bytes(result)


def mesh_string(value):
    encoded = value.encode('utf-8') + b'\0'
    return compact(len(encoded)) + encoded


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def normal(triangle):
    a, b = sub(triangle[1], triangle[0]), sub(triangle[2], triangle[0])
    cross = (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
    length = math.sqrt(sum(n * n for n in cross))
    return tuple(n / length for n in cross) if length > 1e-12 else (0, 0, 1)


def tangent_quaternion(n):
    if n[2] < -0.999999:
        return (1, 0, 0, 0)
    q = (-n[1], n[0], 0, 1 + n[2])
    length = math.sqrt(sum(v * v for v in q))
    return tuple(v / length for v in q)


def bounds(points):
    lower = [min(p[a] for p in points) for a in range(3)]
    upper = [max(p[a] for p in points) for a in range(3)]
    center = [(a + b) / 2 for a, b in zip(lower, upper)]
    radius = max(math.sqrt(sum((p[a] - center[a]) ** 2 for a in range(3))) for p in points)
    return struct.pack('<10f', *lower, *upper, *center, radius)


def surface_data(triangles, center):
    points, tangents, uv = [], [], []
    for triangle in triangles:
        n = normal(triangle)
        axis = max(range(3), key=lambda a: abs(n[a]))
        tangent = tuple(max(-32767, min(32767, int(v * 32767))) for v in tangent_quaternion(n))
        for point in triangle:
            points.append(sub(point, center))
            tangents.append(tangent)
            uv.append((point[(axis + 1) % 3], point[(axis + 2) % 3]))
    count = len(points)
    array = lambda rows, fmt: b''.join(struct.pack('<' + fmt, *row) for row in rows)
    index_format = 'B' if count < 256 else 'H' if count < 65536 else 'I'
    indices = compact(count) + struct.pack('<' + index_format * count, *range(count))
    data = compact(count) + array(points, '3f') + compact(count) + array(tangents, '4h')
    data += compact(0) + compact(count) + array(uv, '2f') + compact(0)
    data += compact(count) + b'\xff' * count * 4 + indices + indices
    return points, data


def encode_mesh(groups, center=(0, 0, 0)):
    surfaces = [(name, *surface_data(triangles, center)) for name, triangles in groups]
    all_points = [point for _, points, _ in surfaces for point in points]
    header = b'ms11' + bounds(all_points) + compact(0) + compact(0) + compact(len(surfaces))
    for name, points, _ in surfaces:
        header += mesh_string(name) + bounds(points) + compact(1) + mesh_string('')
    return header + b'ms11' + b''.join(data for _, _, data in surfaces) + b'ms11'
