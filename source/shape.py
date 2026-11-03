"""Canonical DU voxel identity. Metadata, chunk order and world placement are excluded."""
import base64
import hashlib
import json
import struct

import numpy as np

from cell_codec import decode_cell, decompress, record_hash

MAX_SAMPLES = 300_000


def number(value):
    return int(value['$numberLong']) if isinstance(value, dict) else int(value)


def material_map(cell):
    count = struct.unpack_from('<I', cell.tail)[0]
    result = {}
    for offset in range(count):
        identity, _, reference = struct.unpack_from('<Q8sB', cell.tail, 4 + offset * 17)
        if not reference or reference in result or identity == 0:
            raise ValueError('Invalid material mapper')
        result[reference] = identity
    return result


def samples_from_cell(cell):
    materials = material_map(cell)
    active = np.flatnonzero(np.any(cell.vertices[:, [1, 3, 5]] != 0, axis=1) |
                            (cell.vertices[:, 7] != 255))
    for index in active:
        local = np.unravel_index(index, cell.ranges[3:6])
        point = tuple(int(a + b) for a, b in zip(local, cell.ranges[:3]))
        row = cell.vertices[index]
        weights = {}
        for slot in (0, 2, 4):
            if row[slot + 1]:
                identity = materials[int(row[slot])] if row[slot] else 0
                weights[identity] = weights.get(identity, 0) + int(row[slot + 1])
        position = tuple(int(n) for n in row[7:10]) if row[7] != 255 else (126, 126, 126)
        owned = all(o <= n < o + size for n, o, size in
                    zip(point, cell.ranges[6:9], cell.ranges[9:12]))
        yield point, (tuple(sorted(weights.items())), position, int(row[6]), bool(row[0])), owned


def decode_samples(chunks):
    selected = [c for c in chunks if number(c['k']) == 0 and 'voxel' in c.get('records', {})]
    if not selected:
        raise ValueError('The source has no voxel data')
    lod = min(number(c['h']) for c in selected)
    if lod != 3:
        raise ValueError('Only the ordinary 0.25 m voxel grid is supported')
    samples, priorities = {}, {}
    for chunk in selected:
        if number(chunk['h']) != lod:
            continue
        record = chunk['records']['voxel']
        blob = base64.b64decode(record['data']['$binary'], validate=True)
        if record_hash(blob) != number(record['hash']):
            raise ValueError('Voxel record hash mismatch')
        for point, sample, owned in samples_from_cell(decode_cell(decompress(blob))):
            if point in samples and priorities[point] == 1 and owned and samples[point] != sample:
                raise ValueError('Conflicting samples in overlapping voxel cells')
            if point in samples and priorities[point] == 0 and not owned:
                sample = min(samples[point], sample)
            if priorities.get(point, -1) <= int(owned):
                samples[point], priorities[point] = sample, int(owned)
        if len(samples) > MAX_SAMPLES:
            raise ValueError('Source exceeds the custom element sample limit')
    return samples


def canonicalize(samples, collision_scale=0.05):
    if not 0 < collision_scale <= 1:
        raise ValueError('Collision scale must be within (0, 1]')
    solids = {p: s for p, s in samples.items() if any(i and w for i, w in s[0])}
    if not solids:
        raise ValueError('The source has no honeycomb voxels')
    origin = tuple(min(p[a] for p in solids) for a in range(3))
    relevant = set()
    for p in solids:
        for x in (0, -1):
            for y in (0, -1):
                for z in (0, -1):
                    relevant.add((p[0] + x, p[1] + y, p[2] + z))
    values = []
    for point in sorted(set(solids) | relevant):
        sample = samples.get(point, ((), (126, 126, 126)))
        weights, position = sample[:2]
        flags = sample[2] if len(sample) > 2 else 0
        first_solid = sample[3] if len(sample) > 3 else bool(weights and weights[0][0])
        if point not in solids:
            weights = ()
        if weights or position != (126, 126, 126):
            values.append([*[point[a] - origin[a] for a in range(3)],
                           [list(pair) for pair in sorted(weights)], list(position), flags, first_solid])
    data = {'version': 1, 'voxelSize': 0.25, 'collisionScale': collision_scale, 'samples': values,
            'embeddedElements': [], 'links': []}
    encoded = json.dumps(data, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()
    identity = hashlib.sha256(encoded).hexdigest()
    volumes = {}
    for point in sorted(solids):
        sample = solids[point]
        weights = sample[0]
        flags = sample[2] if len(sample) > 2 else 0
        first_solid = sample[3] if len(sample) > 3 else bool(weights and weights[0][0])
        if flags & 2 or not first_solid:
            continue
        for material, weight in weights:
            if material:
                key = str(material)
                fraction = 1 if len(weights) == 1 else weight / 256
                volumes[key] = volumes.get(key, 0) + 0.25 ** 3 * fraction
    return {'hash': identity, 'canonical': data, 'origin': list(origin),
            'materialVolumes': volumes, 'voxelCount': len(solids)}
