"""Convert server LOD0 geometry into native Unigine mesh, node, material and icon resources."""
import base64
import json
import math
import struct
import xml.etree.ElementTree as ET
from thumbnail import render_icon
from native_mesh import encode_mesh

MAX_TRIANGLES = 100_000
PREFIX = 'resources_generated/custom_elements/'


def glb_document(encoded):
    raw = base64.b64decode(encoded, validate=True)
    magic, version, length = struct.unpack_from('<III', raw)
    if magic != 0x46546C67 or version != 2 or length != len(raw):
        raise ValueError('Expected a complete GLB 2 mesh')
    offset, document, binary = 12, None, None
    while offset < length:
        size, kind = struct.unpack_from('<II', raw, offset)
        offset += 8
        if offset + size > length:
            raise ValueError('Truncated GLB chunk')
        data, offset = raw[offset:offset + size], offset + size
        if kind == 0x4E4F534A:
            document = json.loads(data)
        elif kind == 0x004E4942:
            binary = data
    if document is None or binary is None:
        raise ValueError('Missing GLB JSON or binary chunk')
    return document, binary


def accessor(document, binary, index):
    value = document['accessors'][index]
    view = document['bufferViews'][value['bufferView']]
    formats = {5126: ('f', 4), 5125: ('I', 4), 5123: ('H', 2), 5121: ('B', 1)}
    counts = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}
    if value.get('sparse') or value.get('normalized') or view.get('buffer', 0) != 0:
        raise ValueError('Unsupported GLB accessor encoding')
    code, size = formats[value['componentType']]
    components = counts[value['type']]
    packed = size * components
    stride = view.get('byteStride', packed)
    start = view.get('byteOffset', 0) + value.get('byteOffset', 0)
    end = start + max(0, value['count'] - 1) * stride + packed
    if stride < packed or end > view.get('byteOffset', 0) + view['byteLength'] or end > len(binary):
        raise ValueError('GLB accessor extends beyond its buffer')
    result = [struct.unpack_from('<' + code * components, binary, start + n * stride)
              for n in range(value['count'])]
    if any(not math.isfinite(float(v)) for row in result for v in row):
        raise ValueError('Non-finite GLB geometry')
    return result


def triangles_from_glb(encoded):
    document, binary = glb_document(encoded)
    for node in document.get('nodes', []):
        if any(k in node for k in ('matrix', 'translation', 'rotation', 'scale')):
            raise ValueError('Transformed GLB nodes require an explicit conversion')
    triangles = []
    for mesh in document['meshes']:
        for primitive in mesh['primitives']:
            if primitive.get('mode', 4) != 4:
                raise ValueError('Only triangle meshes are supported')
            positions = accessor(document, binary, primitive['attributes']['POSITION'])
            indices = [row[0] for row in accessor(document, binary, primitive['indices'])]
            if len(indices) % 3 or any(i >= len(positions) for i in indices):
                raise ValueError('Invalid triangle indices')
            material = document['materials'][primitive['material']]['name']
            if not material.isdecimal():
                raise ValueError('Expected native DU material IDs')
            for n in range(0, len(indices), 3):
                triangles.append((material, tuple(positions[i] for i in indices[n:n + 3])))
            if len(triangles) > MAX_TRIANGLES:
                raise ValueError('Custom element exceeds the triangle limit')
    if not triangles:
        raise ValueError('The voxel service has not produced a mesh for this source')
    return triangles


def text(values):
    return ' '.join(format(float(v), '.9g') for v in values)


def mesh_node(parent, name, mesh_path, materials, enabled=True):
    node = ET.SubElement(parent, 'node', type='ObjectMeshStatic', name=name, enabled='1' if enabled else '0')
    ET.SubElement(node, 'mesh_name').text = mesh_path
    for material in materials:
        ET.SubElement(node, 'surface', name=material, material=material, property='surface_base')
    ET.SubElement(node, 'transform').text = '1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1'
    return node


def box_triangles(dimensions):
    points = [tuple(dimensions[a] * (1 if mask & (1 << a) else -1) / 2 for a in range(3))
              for mask in range(8)]
    faces = ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5))
    return [tuple(points[f[i]] for i in tri) for f in faces for tri in ((0, 1, 2), (0, 2, 3))]


def build_assets(shape, encoded_mesh, material_definitions):
    return build_triangle_assets(shape, triangles_from_glb(encoded_mesh), material_definitions)


def build_triangle_assets(shape, triangles, material_definitions):
    points = [p for _, triangle in triangles for p in triangle]
    lower = [min(p[a] for p in points) for a in range(3)]
    upper = [max(p[a] for p in points) for a in range(3)]
    center = [(lower[a] + upper[a]) / 2 for a in range(3)]
    dimensions = [upper[a] - lower[a] for a in range(3)]
    if any(n <= 0 or n > 64 for n in dimensions):
        raise ValueError('Invalid custom element visual bounds')
    collision = dimensions
    stem, name = shape['hash'], shape.get('name', 'CustomVoxel' + shape['hash'])
    groups = {}
    for identity, triangle in triangles:
        groups.setdefault('ce_' + stem[:12] + '_' + identity, []).append(triangle)
    materials = material_library(stem, groups, material_definitions)
    root = ET.Element('nodes', version='2.3')
    library = ET.SubElement(root, 'materials')
    ET.SubElement(library, 'library').text = PREFIX + stem + '.mat'
    container = mesh_node(root, 'visual', PREFIX + stem + '.mesh', groups)
    mesh_node(container, 'col_mesh', PREFIX + stem + '_col.r4.mesh', groups, enabled=False)
    definition = {'elements': {name: {'node': PREFIX + stem + '.r4.node', 'icon': PREFIX + stem + '.r4.png',
                  'soundMaterial': 'hard', 'colMesh': 'col_mesh'}}}
    shape.update(name=name, visualBounds={'min': [-n / 2 for n in dimensions],
                 'max': [n / 2 for n in dimensions]}, collisionBounds={'min': [-n / 2 for n in collision],
                 'max': [n / 2 for n in collision]}, sourceMeshCenter=center)
    shape['renderRevision'] = 4
    shape['iconRevision'] = 2
    mesh = encode_mesh(list(groups.items()), center)
    return {stem + '.r4.node': ET.tostring(root, encoding='utf-8', xml_declaration=True),
            stem + '.mat': materials, stem + '.nqdef': json.dumps(definition).encode(),
            stem + '.r4.png': render_icon(triangles, material_definitions),
            stem + '.mesh': mesh, stem + '_col.r4.mesh': mesh}


def material_library(stem, groups, definitions):
    root = ET.Element('materials', version='2.3')
    for name in groups:
        identity = name.rsplit('_', 1)[1]
        if identity not in definitions:
            raise ValueError('Missing client material definition for ' + identity)
        definition = definitions[identity]
        material = ET.SubElement(root, 'material', name=name, parent='mesh_base')
        ET.SubElement(material, 'state', name='nq_mrao').text = '1'
        # mesh_base multiplies the MRAO metal channel by this parameter.
        # Native DU mesh materials explicitly use 1; the default loses metallic reflections.
        ET.SubElement(material, 'parameter', name='metalness').text = '1'
        ET.SubElement(material, 'parameter', name='roughness').text = '1'
        tint = definition.get('albedo', [1, 1, 1])
        ET.SubElement(material, 'parameter', name='albedo_color').text = text([*tint[:3], 1])
        texture = definition.get('texture')
        if texture:
            for slot, suffix in (('albedo', 'c'), ('normal', 'n'), ('shading', 'mrao')):
                ET.SubElement(material, 'texture', name=slot).text = texture + '_' + suffix + '.dds'
    return ET.tostring(root, encoding='utf-8', xml_declaration=True)
