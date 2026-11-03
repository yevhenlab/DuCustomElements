import base64
import json
import struct
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zlib
from pathlib import Path

from geometry import box_triangles, build_assets, build_triangle_assets, triangles_from_glb
from registry import Registry, item_id
from shape import canonicalize
from test_native_mesh import MeshReader
from modules import include_elements, center_elements


def sample(weights=((123, 255),), position=(126, 126, 126)):
    return weights, position


def simple_glb():
    points = [p for triangle in box_triangles((1, 1, 2)) for p in triangle]
    binary = b''.join(struct.pack('<3f', *p) for p in points)
    split = len(binary)
    binary += struct.pack('<' + 'I' * len(points), *range(len(points)))
    document = {'asset': {'version': '2.0'}, 'buffers': [{'byteLength': len(binary)}],
                'bufferViews': [{'buffer': 0, 'byteOffset': 0, 'byteLength': split},
                                {'buffer': 0, 'byteOffset': split, 'byteLength': len(points) * 4}],
                'accessors': [{'bufferView': 0, 'componentType': 5126, 'count': len(points), 'type': 'VEC3'},
                              {'bufferView': 1, 'componentType': 5125, 'count': len(points), 'type': 'SCALAR'}],
                'materials': [{'name': '123'}], 'meshes': [{'primitives': [
                    {'attributes': {'POSITION': 0}, 'indices': 1, 'material': 0}]}], 'nodes': [{'mesh': 0}]}
    encoded = json.dumps(document, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    return base64.b64encode(struct.pack('<III', 0x46546C67, 2, 28 + len(encoded) + len(binary)) +
                            struct.pack('<II', len(encoded), 0x4E4F534A) + encoded +
                            struct.pack('<II', len(binary), 0x004E4942) + binary).decode()


class CanonicalTests(unittest.TestCase):
    def test_translation_order_and_irrelevant_air_do_not_change_identity(self):
        original = {(12, 4, 8): sample(), (13, 4, 8): sample(), (11, 4, 8): sample((), (125, 126, 126))}
        translated = {tuple(v + d for v, d in zip(p, (40, -5, 9))): value
                      for p, value in reversed(list(original.items()))}
        translated[(999, 999, 999)] = sample((), (1, 2, 3))
        a, b = canonicalize(original), canonicalize(translated)
        self.assertEqual(a['hash'], b['hash'])
        self.assertEqual(a['materialVolumes'], b['materialVolumes'])

    def test_material_deformation_and_collision_affect_identity(self):
        source = {(0, 0, 0): sample()}
        identity = canonicalize(source)['hash']
        self.assertNotEqual(identity, canonicalize({(0, 0, 0): sample(((124, 255),))})['hash'])
        self.assertNotEqual(identity, canonicalize({(0, 0, 0): sample(position=(126, 126, 125))})['hash'])
        self.assertNotEqual(identity, canonicalize(source, collision_scale=1)['hash'])

    def test_weight_order_does_not_change_identity(self):
        a = canonicalize({(0, 0, 0): sample(((123, 100), (124, 155)))})
        b = canonicalize({(0, 0, 0): sample(((124, 155), (123, 100)))})
        self.assertEqual(a['hash'], b['hash'])

    def test_empty_source_is_rejected(self):
        with self.assertRaises(ValueError):
            canonicalize({})

    def test_native_material_volume_uses_256_mixed_weights_and_skips_flagged_samples(self):
        mixed = canonicalize({(0, 0, 0): sample(((123, 128), (124, 127)))})
        self.assertEqual(mixed['materialVolumes']['123'], 0.25 ** 3 / 2)
        self.assertEqual(mixed['materialVolumes']['124'], 0.25 ** 3 * 127 / 256)
        flagged = canonicalize({(0, 0, 0): (*sample(), 2, True)})
        self.assertEqual(flagged['materialVolumes'], {})
        self.assertNotEqual(flagged['hash'], canonicalize({(0, 0, 0): sample()})['hash'])

    def test_ordinary_game_item_hash_matches_live_definitions(self):
        self.assertEqual(item_id('CoreUnitDynamic64'), 183890525)
        self.assertEqual(item_id('CoreUnitDynamic128'), 1418170469)
        self.assertEqual(item_id('CoreUnitDynamic64'), item_id('coreunitdynamic64'))


class AssetTests(unittest.TestCase):
    def test_s_core_visual_bounds_and_oversize_rejection(self):
        shape = canonicalize({(0, 0, 0): sample()})
        triangles = [('123', triangle) for triangle in box_triangles((48, 2, 1))]
        assets = build_triangle_assets(shape, triangles, {'123': {'albedo': [1, 1, 1]}})
        self.assertIn(shape['hash'] + '.mesh', assets)
        self.assertEqual(shape['visualBounds']['max'][0], 24)
        with self.assertRaisesRegex(ValueError, 'visual bounds'):
            build_triangle_assets(shape, [('123', t) for t in box_triangles((65, 2, 1))], {})

    def test_saved_shapes_upgrade_without_changing_existing_item_types(self):
        fixtures = list((Path(__file__).parent / 'test-output/registry-before-r3').glob('*.json'))
        if not fixtures:
            self.skipTest('Live registry backup is not mounted')
        with tempfile.TemporaryDirectory() as folder:
            for fixture in fixtures:
                original = json.loads(fixture.read_text())
                destination = Path(folder) / fixture.name
                destination.write_text(json.dumps(original))
                registry = Registry(folder, {})
                shape = registry.read_shape(destination)
                self.assertEqual((shape['hash'], shape['itemTypeId'], shape['name']),
                                 (original['hash'], original['itemTypeId'], original['name']))
                self.assertEqual(shape['canonical'], original['canonical'])
                assets = registry.unpack_assets(shape)
                self.assertTrue(assets[shape['hash'] + '.r4.png'].startswith(b'\x89PNG'))
                self.assertGreater(len(MeshReader(assets[shape['hash'] + '.mesh']).surfaces()), 0)
                self.assertEqual(registry.read_shape(destination), shape)

    def test_real_dimensions_and_full_geometry_collider(self):
        shape = canonicalize({(0, 0, 0): sample()})
        assets = build_assets(shape, simple_glb(), {'123': {'albedo': [0.5, 0.2, 0.1]}})
        bounds = shape['visualBounds']
        self.assertEqual([b - a for a, b in zip(bounds['min'], bounds['max'])], [1, 1, 2])
        self.assertEqual(shape['collisionBounds']['max'], [0.5, 0.5, 1])
        self.assertEqual(assets[shape['hash'] + '.mesh'], assets[shape['hash'] + '_col.r4.mesh'])
        root = ET.fromstring(assets[shape['hash'] + '.r4.node'])
        self.assertIsNotNone(root.find(".//node[@name='col_mesh']"))
        self.assertEqual(len(triangles_from_glb(simple_glb())), 12)

    def test_missing_material_is_rejected(self):
        with self.assertRaises(ValueError):
            build_assets(canonicalize({(0, 0, 0): sample()}), simple_glb(), {})

    def test_packaged_node_uses_native_compression_and_definition_is_last(self):
        packaged = Registry.package({'a.node': b'<nodes/>', 'a.nqdef': b'{}'})
        self.assertEqual([item['kind'] for item in packaged], [2, 1])
        blob = base64.b64decode(packaged[0]['data'])
        self.assertEqual(struct.unpack_from('<IQ', blob), (0x124F0359, 8))
        self.assertEqual(zlib.decompress(blob[12:]), b'<nodes/>')

    def test_collision_resolution_does_not_reuse_an_existing_type(self):
        shape = canonicalize({(0, 0, 0): sample()})
        first = 'CustomVoxel' + shape['hash']
        with tempfile.TemporaryDirectory() as folder:
            registry = Registry(folder, {})
            registry.allocate_name(shape, {str(item_id(first)): 'UnrelatedItem'})
            self.assertNotEqual(shape['name'], first)
            self.assertEqual(shape['itemTypeId'], str(item_id(shape['name'])))

    def test_stale_mesh_is_rejected(self):
        with self.assertRaises(ValueError):
            Registry.check_mesh_freshness({'cells': [], 'meshes': {'Meta': {
                'basedOn': {'networkTime': 1}, 'youngestCellDate': {'networkTime': 2}}}})


class ModuleTests(unittest.TestCase):
    def element(self, position=(3, 4, 5)):
        return {'key': 1, 'itemTypeId': 1234, 'position': list(position), 'rotation': [0, 0, 0, 1],
                'properties': {'dpuyaml_6': {'type': 4, 'value': 'unit.start: test()'}}}

    def test_module_identity_changes_with_configuration_and_links(self):
        a = include_elements(None, [self.element()], [])
        changed = self.element()
        changed['properties']['dpuyaml_6']['value'] = 'unit.start: changed()'
        self.assertNotEqual(a['hash'], include_elements(None, [changed], [])['hash'])
        self.assertNotEqual(a['hash'], include_elements(None, [self.element()],
            [{'fromElementId': 1, 'toElementId': 0, 'fromPlug': 1, 'toPlug': 2, 'plugType': 1}])['hash'])

    def test_voxel_and_element_translation_preserves_identity(self):
        a = include_elements(canonicalize({(8, 8, 8): sample()}), [self.element()], [])
        b = include_elements(canonicalize({(12, 12, 12): sample()}), [self.element((4, 5, 6))], [])
        self.assertEqual(a['hash'], b['hash'])
        a['sourceMeshCenter'] = [3, 4, 5]
        center_elements(a)
        self.assertEqual(a['embeddedElements'][0]['position'], [0, 0, 0])

    def test_element_only_registry_preserves_lua_and_stable_item_identity(self):
        with tempfile.TemporaryDirectory() as folder:
            registry = Registry(folder, {})
            request = {'cells': [], 'embeddedElements': [self.element()], 'materialNames': {}}
            a = registry.prepare(request)
            b = registry.prepare(request)
            self.assertEqual(a['itemTypeId'], b['itemTypeId'])
            self.assertEqual(a['embeddedElements'][0]['properties'], self.element()['properties'])
            self.assertEqual(a['embeddedElements'][0]['position'], [0, 0, 0])
            self.assertEqual(a['materialVolumes'], {})

    def test_invalid_rotation_and_external_links_are_rejected(self):
        element = self.element()
        element['rotation'] = [0, 0, 0, 0]
        with self.assertRaisesRegex(ValueError, 'rotation'):
            include_elements(None, [element], [])
        with self.assertRaisesRegex(ValueError, 'outside'):
            include_elements(None, [self.element()], [{'fromElementId': 1, 'toElementId': 99}])


if __name__ == '__main__':
    unittest.main()

