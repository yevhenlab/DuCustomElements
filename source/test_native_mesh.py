"""Independent ms11 reader checks against the official fixture and generated multi-material meshes."""
import io
import struct
import unittest
from pathlib import Path

from native_mesh import compact, encode_mesh


class MeshReader:
    def __init__(self, data):
        self.stream = io.BytesIO(data)

    def integer(self):
        byte = self.stream.read(1)[0]
        result, shift, negative = byte & 63, 6, bool(byte & 64)
        while byte & 128:
            byte = self.stream.read(1)[0]
            result |= (byte & 127) << shift
            shift += 7
        return -result if negative else result

    def string(self):
        return self.stream.read(self.integer())[:-1].decode()

    def surfaces(self):
        assert self.stream.read(4) == b'ms11'
        self.stream.read(40)
        assert self.integer() == 0  # no bones
        assert self.integer() == 0  # no animations
        names = []
        for _ in range(self.integer()):
            names.append(self.string())
            self.stream.read(40)
            assert self.integer() == 1
            self.string()
        assert self.stream.read(4) == b'ms11'
        result = []
        for name in names:
            vertices = self.integer()
            positions = struct.unpack('<' + '3f' * vertices, self.stream.read(vertices * 12))
            tangents = self.integer()
            self.stream.read(tangents * 8)
            assert self.integer() == 0
            for width in (8, 8, 4):  # UV0, UV1, colors
                self.stream.read(self.integer() * width)
            coordinate_count = self.integer()
            self.stream.read(coordinate_count * (1 if vertices < 256 else 2 if vertices < 65536 else 4))
            triangle_count = self.integer()
            size = 1 if coordinate_count < 256 else 2 if coordinate_count < 65536 else 4
            self.stream.read(triangle_count * size)
            result.append((name, vertices, triangle_count, positions))
        assert self.stream.read() == b'ms11'
        return result


class NativeMeshTests(unittest.TestCase):
    def test_compact_integer_boundaries(self):
        for value in (0, 1, 63, 64, 8191, 8192, 54227, 75496, 326196, 0x7fffffff):
            self.assertEqual(MeshReader(compact(value)).integer(), value)
        self.assertEqual(compact(54227), b'\x93\xcf\x06')  # official fixture's vertex count

    def test_independent_reader_accepts_official_sample(self):
        path = Path(__file__).parent.parent / 'tmp/mydu-server-mods-reference/ModButtonInteraction/ClientSide'
        if not (path / 'CustomButtonAFixed.mesh').exists():
            self.skipTest('Official sample is not mounted')
        surfaces = MeshReader((path / 'CustomButtonAFixed.mesh').read_bytes()).surfaces()
        self.assertEqual([(s[1], s[2]) for s in surfaces], [(54227, 326196)])

    def test_multiple_materials_have_independent_geometry_and_index_arrays(self):
        triangle = ((0, 0, 0), (1, 0, 0), (0, 1, 1))
        surfaces = MeshReader(encode_mesh([('steel', [triangle]), ('wood', [triangle] * 100)])).surfaces()
        self.assertEqual([(s[0], s[1], s[2]) for s in surfaces], [('steel', 3, 3), ('wood', 300, 300)])
        self.assertEqual(surfaces[0][3], tuple(float(v) for point in triangle for v in point))


if __name__ == '__main__':
    unittest.main()
