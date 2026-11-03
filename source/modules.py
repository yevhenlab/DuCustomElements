"""Stable module templates; native child instances and runtime IDs are never part of a mesh asset."""
import hashlib
import json
import math


def vector(value, count):
    if not isinstance(value, list) or len(value) != count:
        raise ValueError('Invalid embedded element transform')
    if any(not isinstance(n, (int, float)) or not math.isfinite(n) for n in value):
        raise ValueError('Invalid embedded element transform')
    return [round(float(n), 8) for n in value]


def include_elements(shape, elements, links):
    if not elements:
        return shape
    if len(elements) > 128:
        raise ValueError('A module supports up to 128 embedded elements')
    if shape is None:
        origin = [min(vector(e['position'], 3)[a] for e in elements) for a in range(3)]
        shape = {'origin': [n / 0.25 for n in origin], 'materialVolumes': {}, 'voxelCount': 0,
                 'canonical': {'version': 2, 'voxelSize': 0.25, 'collisionScale': 0.05,
                               'samples': [], 'embeddedElements': [], 'links': []}}
    origin = [n * 0.25 for n in shape['origin']]
    embedded = []
    for element in elements:
        item_type, key = int(element['itemTypeId']), int(element['key'])
        if item_type <= 0 or key <= 0:
            raise ValueError('Invalid embedded element identity')
        position = vector(element['position'], 3)
        rotation = vector(element['rotation'], 4)
        if abs(sum(n * n for n in rotation) - 1) > 0.001:
            raise ValueError('Embedded element rotation is not normalized')
        embedded.append({'key': key, 'itemTypeId': item_type,
                         'position': [round(p - o, 8) for p, o in zip(position, origin)],
                         'rotation': rotation, 'properties': element.get('properties', {})})
    keys = {e['key'] for e in embedded}
    if len(keys) != len(embedded):
        raise ValueError('Duplicate embedded element key')
    for link in links:
        if link['fromElementId'] not in keys | {0} or link['toElementId'] not in keys | {0}:
            raise ValueError('Embedded link points outside the module')
    shape['canonical'].update(version=2, embeddedElements=embedded,
                              links=sorted(links, key=lambda l: json.dumps(l, sort_keys=True)))
    data = json.dumps(shape['canonical'], sort_keys=True, separators=(',', ':'), allow_nan=False).encode()
    shape['hash'] = hashlib.sha256(data).hexdigest()
    return shape


def center_elements(shape):
    origin = [n * 0.25 for n in shape['origin']]
    center = shape['sourceMeshCenter']
    shape['embeddedElements'] = [dict(e, position=[p + o - c for p, o, c in
                                                  zip(e['position'], origin, center)])
                                 for e in shape['canonical']['embeddedElements']]
    shape['embeddedLinks'] = shape['canonical']['links']
