"""Read-only subset of the project's validated DU cell codec.

Adapted from the VoxelsFromOutside DU voxel codec.
"""
import struct
from dataclasses import dataclass

import lz4.block
import numpy as np
import xxhash


def decompress(blob):
    magic, length = struct.unpack_from('<IQ', blob)
    if magic != 0xFB14B6F9 or not 0 < length <= 16_000_000:
        raise ValueError('Unsupported voxel wrapper or excessive decoded length')
    decoded = lz4.block.decompress(blob[12:], uncompressed_size=length)
    if len(decoded) != length:
        raise ValueError('Voxel decoded length mismatch')
    return decoded


def record_hash(blob):
    value = xxhash.xxh64_intdigest(blob, seed=0xA1B2C3D4E5F6E7D8)
    return value if value < 2**63 else value - 2**64


class Reader:
    def __init__(self, raw, offset):
        self.raw, self.offset = raw, offset

    def take(self, length):
        end = self.offset + length
        if length < 0 or end > len(self.raw):
            raise ValueError('Truncated voxel cell')
        result, self.offset = self.raw[self.offset:end], end
        return result

    def byte(self):
        return self.take(1)[0]


def read_materials(reader, grid):
    index = 0
    while index < len(grid):
        count = reader.byte()
        if count > 3:
            raise ValueError('Invalid material count')
        material, total = [0] * 6, 0
        for slot in range(count):
            material[slot * 2] = reader.byte()
            weight = reader.byte() if slot < count - 1 else (255 if count == 1 else -total & 255)
            material[slot * 2 + 1], total = weight, total + weight
        repeat = reader.byte() + 1
        if index + repeat > len(grid):
            raise ValueError('Material run exceeds voxel grid')
        grid[index:index + repeat, :6] = material
        index += repeat


def read_positions(reader, grid, version):
    index = 0
    while index < len(grid):
        flags, stop = reader.byte(), index + reader.byte() + 1
        if stop > len(grid) or flags & ~3:
            raise ValueError('Invalid vertex flags or run')
        grid[index:stop, 6] = flags & ~1
        if not flags & 1:
            index = stop
            continue
        while index < stop:
            position = list(reader.take(3))
            repeat = reader.byte() + 1 if version == 9 else 1
            if index + repeat > stop:
                raise ValueError('Position run exceeds flag run')
            if version == 7 and position[0] != 255:
                position = [min(max(value, 2) - 2, 252) for value in position]
            grid[index:index + repeat, 7:10] = position
            index += repeat


@dataclass
class Cell:
    ranges: tuple
    vertices: np.ndarray
    tail: bytes


def decode_cell(raw):
    magic, cell_version, grid_magic, version = struct.unpack_from('<4I', raw)
    if (magic, cell_version, grid_magic) != (0x27B8A013, 6, 0xE881339E) or version not in (7, 8, 9):
        raise ValueError('Unsupported DU cell format')
    reader = Reader(raw, 16)
    ranges = struct.unpack('<12i', reader.take(48))
    if any(size <= 0 or size > 1024 for size in ranges[3:6]):
        raise ValueError('Invalid voxel grid dimensions')
    count = int(np.prod(ranges[3:6], dtype=np.int64))
    if not 0 < count <= 1_000_000:
        raise ValueError('Excessive voxel grid dimensions')
    grid = np.zeros((count, 10), dtype=np.uint8)
    grid[:, 7:10] = 255
    read_materials(reader, grid)
    read_positions(reader, grid, version)
    tail = raw[reader.offset:]
    mapper_count = struct.unpack_from('<I', tail)[0]
    if len(tail) != 4 + mapper_count * 17 + 1:
        raise ValueError('Unexpected material mapper tail')
    return Cell(ranges, grid, tail)

