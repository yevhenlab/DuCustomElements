"""Immutable, crash-safe content registry. Full hashes are retained to check ID collisions."""
import base64
import json
import os
import re
import struct
import threading
import zlib
import xml.etree.ElementTree as ET
from pathlib import Path

from geometry import PREFIX, box_triangles, build_assets, build_triangle_assets
from modules import include_elements, center_elements
from shape import canonicalize, decode_samples


def item_id(name):
    value = 0
    for char in name.lower():
        if '0' <= char <= '9' or 'a' <= char <= 'z':
            value ^= (ord(char) + 0x9E3779B9 + (value << 6) + (value >> 2)) & 0xffffffff
    return value


def atomic_json(path, value):
    temporary = path.with_suffix('.pending')
    with temporary.open('w', encoding='utf-8') as stream:
        json.dump(value, stream, separators=(',', ':'), allow_nan=False)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


class Registry:
    def __init__(self, folder, client_definitions):
        self.folder = Path(folder)
        self.folder.mkdir(parents=True, exist_ok=True)
        self.definitions = client_definitions
        self.lock = threading.RLock()
        icons = Path(__file__).with_name('element-icons.json')
        self.element_icons = json.loads(icons.read_text()) if icons.exists() else {}

    def prepare(self, request):
        elements, links = request.get('embeddedElements') or [], request.get('embeddedLinks') or []
        try:
            shape = canonicalize(decode_samples(request.get('cells') or []))
        except ValueError as error:
            if not elements or str(error) not in ('The source has no voxel data', 'The source has no honeycomb voxels'):
                raise
            shape = None
        shape = include_elements(shape, elements, links)
        path = self.folder / (shape['hash'] + '.json')
        with self.lock:
            if path.exists():
                saved = self.read_shape(path)
                if saved['canonical'] != shape['canonical']:
                    raise ValueError('Content hash collision')
                return saved
            definitions = self.materials(request['materialNames'])
            meshes = (request.get('meshes') or {}).get('Meshes') or []
            if not meshes and shape['voxelCount']:
                raise ValueError('Source mesh unavailable; wait for the voxel service to mesh the construct')
            if shape['voxelCount']:
                self.check_mesh_freshness(request)
                assets = build_assets(shape, meshes[0], definitions)
            else:
                assets = self.element_only_assets(shape, elements)
            center_elements(shape)
            self.allocate_name(shape, request.get('reservedIds', {}))
            # An ID collision may change the name, so update only the public element name.
            definition_path = shape['hash'] + '.nqdef'
            definition = json.loads(assets[definition_path])
            entry = next(iter(definition['elements'].values()))
            assets[definition_path] = json.dumps({'elements': {shape['name']: entry}}).encode()
            shape['assets'] = self.package(assets)
            shape['status'] = 'prepared'
            atomic_json(path, shape)
            return shape

    def element_only_assets(self, shape, elements):
        triangles = [('0', t) for t in box_triangles((0.01, 0.01, 0.01))]
        assets = build_triangle_assets(shape, triangles, {'0': {'albedo': [0.3, 0.7, 1]}})
        stem = shape['hash']
        root = ET.fromstring(assets[stem + '.r4.node'])
        root.find(".//node[@name='visual']").set('enabled', '0')
        assets[stem + '.r4.node'] = ET.tostring(root, encoding='utf-8', xml_declaration=True)
        shape['sourceMeshCenter'] = [n * 0.25 for n in shape['origin']]
        icon = self.element_icons.get(elements[0].get('assetAlias'))
        if icon:
            definition = json.loads(assets[stem + '.nqdef'])
            next(iter(definition['elements'].values()))['icon'] = icon
            assets[stem + '.nqdef'] = json.dumps(definition).encode()
        return assets

    def materials(self, names):
        result = {}
        for identity, aliases in names.items():
            if not isinstance(aliases, list):
                aliases = [aliases]
            definition = next((self.definitions[a] for a in aliases if a in self.definitions), None)
            if definition is not None:
                result[str(identity)] = definition
        return result

    def allocate_name(self, shape, reserved):
        occupied = {str(d['itemTypeId']): d['hash'] for d in self.list_shapes()}
        for attempt in range(1024):
            name = 'CustomVoxel' + shape['hash'] + (str(attempt) if attempt else '')
            identity = str(item_id(name))
            if identity == '0':
                continue
            if identity in occupied and occupied[identity] != shape['hash']:
                continue
            if identity in reserved and reserved[identity] != name:
                continue
            shape.update(name=name, itemTypeId=identity)
            return
        raise ValueError('Unable to allocate an unoccupied item type ID')

    @staticmethod
    def check_mesh_freshness(request):
        meta = request.get('meshes', {}).get('Meta', {})
        based_on = meta.get('basedOn', {}).get('networkTime')
        youngest = meta.get('youngestCellDate', {}).get('networkTime')
        if based_on is None or youngest is None or based_on < youngest:
            raise ValueError('Source mesh is not current')
        # Cell modification dates are ISO timestamps; compare to native mesh revision time.
        from datetime import datetime
        for cell in request['cells']:
            date = cell.get('updated_at', {}).get('$date')
            if date and datetime.fromisoformat(date.replace('Z', '+00:00')).timestamp() * 1000 > based_on + 1:
                raise ValueError('Source changed after its mesh was generated')

    @staticmethod
    def package(assets):
        packaged = []
        for name, data in sorted(assets.items(), key=lambda pair: pair[0].endswith('.nqdef')):
            definition = name.endswith('.nqdef')
            if not definition:
                data = struct.pack('<IQ', 0x124F0359, len(data)) + zlib.compress(data, 9)
                name += '.nqz'
            if len(data) > 2_000_000:
                raise ValueError('Generated resource exceeds the streaming message limit')
            packaged.append({'path': PREFIX + name, 'kind': 1 if definition else 2,
                             'data': base64.b64encode(data).decode()})
        return packaged

    def list_shapes(self):
        with self.lock:
            return [self.read_shape(path) for path in sorted(self.folder.glob('*.json'))
                    if re.fullmatch(r'[a-f0-9]{64}\.json', path.name)]

    def read_shape(self, path):
        shape = json.loads(path.read_text(encoding='utf-8'))
        if shape.get('renderRevision', 0) < 3:
            self.upgrade_assets(shape)
            atomic_json(path, shape)
        if shape.get('renderRevision', 0) < 4:
            self.upgrade_collision(shape)
            atomic_json(path, shape)
        if shape.get('iconRevision', 0) < 2:
            self.upgrade_icon(shape)
            atomic_json(path, shape)
        return shape

    def upgrade_icon(self, shape):
        assets = self.unpack_assets(shape)
        stem = shape['hash']
        definition = json.loads(assets[stem + '.nqdef'])
        entry = definition['elements'][shape['name']]
        old_name = entry['icon'].removeprefix(PREFIX)
        if old_name in assets:
            new_name = stem + '.r4.png'
            assets[new_name] = assets.pop(old_name)
            entry['icon'] = PREFIX + new_name
            assets[stem + '.nqdef'] = json.dumps(definition).encode()
        shape.update(assets=self.package(assets), iconRevision=2)

    def upgrade_collision(self, shape):
        assets = self.unpack_assets(shape)
        stem = shape['hash']
        root = ET.fromstring(assets.pop(stem + '.r3.node'))
        collider = root.find(".//node[@name='col_mesh']")
        collider.find('mesh_name').text = PREFIX + stem + '_col.r4.mesh'
        visual = root.find(".//node[@name='visual']")
        for surface in list(collider.findall('surface')):
            collider.remove(surface)
        for surface in visual.findall('surface'):
            ET.SubElement(collider, 'surface', **surface.attrib)
        assets.pop(stem + '_col.mesh')
        assets[stem + '_col.r4.mesh'] = assets[stem + '.mesh']
        assets[stem + '.r4.node'] = ET.tostring(root, encoding='utf-8', xml_declaration=True)
        definition = json.loads(assets[stem + '.nqdef'])
        definition['elements'][shape['name']]['node'] = PREFIX + stem + '.r4.node'
        assets[stem + '.nqdef'] = json.dumps(definition).encode()
        shape.update(assets=self.package(assets), collisionBounds=shape['visualBounds'], renderRevision=4)

    @staticmethod
    def unpack_assets(shape):
        assets = {}
        for entry in shape['assets']:
            name = entry['path'].removeprefix(PREFIX)
            data = base64.b64decode(entry['data'], validate=True)
            if name.endswith('.nqz'):
                magic, size = struct.unpack_from('<IQ', data)
                data = zlib.decompress(data[12:])
                if magic != 0x124F0359 or size != len(data):
                    raise ValueError('Invalid saved resource compression')
                name = name[:-4]
            assets[name] = data
        return assets

    def upgrade_assets(self, shape):
        assets = self.unpack_assets(shape)
        stem = shape['hash']
        root = ET.fromstring(assets[stem + '.node'])
        visual = root.find(".//node[@name='visual']")
        values = list(map(float, visual.find("vertex[@offset='0']").text.split()))
        points = [tuple(values[n:n + 3]) for n in range(0, len(values), 3)]
        indices = list(map(int, visual.find('indices').text.split()))
        materials, triangles = {}, []
        library = ET.fromstring(assets[stem + '.mat'])
        for surface in visual.findall('object_surface'):
            name = surface.attrib['name']
            identity = name.rsplit('_', 1)[1]
            tint = library.find(f"material[@name='{name}']/parameter[@name='albedo_color']")
            materials[identity] = {'albedo': list(map(float, tint.text.split()))[:3]}
            texture = library.find(f"material[@name='{name}']/texture[@name='albedo']")
            if texture is not None:
                materials[identity]['texture'] = texture.text.removesuffix('_c.dds')
            start, end = int(surface.attrib['begin']), int(surface.attrib['end'])
            triangles.extend((identity, tuple(points[indices[i]] for i in range(n, n + 3)))
                             for n in range(start, end, 3))
        original_center = shape['sourceMeshCenter']
        assets = build_triangle_assets(shape, triangles, materials)
        shape.update(assets=self.package(assets), sourceMeshCenter=original_center)


def load_material_definitions(path):
    document = json.loads(Path(path).read_text(encoding='utf-8-sig'))
    return {name: definition for group in document['worlds']['voxel']['materialDefinitions'].values()
            for name, definition in group.get('materials', {}).items()}
