import json
import struct
from pathlib import Path

def read_catalog(path):
    data = path.read_bytes()
    magic, table_bytes, count = struct.unpack_from('<iii', data)
    if count < 2 or 12 + count * 4 > len(data):
        raise ValueError('Invalid FileBlocks count')
    if not (magic == 112233 and table_bytes == 4 + count * 4):
        raise ValueError('Invalid MapPro structure')
    lengths = struct.unpack_from('<' + 'i' * count, data, 12)
    if not all((length >= 0 for length in lengths)):
        raise ValueError('Invalid MapPro structure')
    if not 8 + table_bytes + sum(lengths) == len(data):
        raise ValueError('Invalid MapPro structure')
    signature = struct.pack('<ii', 11223344, 17) + b'MapPro_FileBlocks'
    if data[8 + table_bytes:8 + table_bytes + lengths[0]] != signature:
        raise ValueError('Invalid FileBlocks signature')
    start = 8 + table_bytes + sum(lengths[:-1])
    end = start + lengths[-1]
    cursor = start

    def unpack(fmt):
        nonlocal cursor
        size = struct.calcsize(fmt)
        if not cursor + size <= end:
            raise ValueError('Invalid MapPro structure')
        values = struct.unpack_from(fmt, data, cursor)
        cursor += size
        return values

    def integer():
        return unpack('<i')[0]

    def string():
        nonlocal cursor
        size = integer()
        if not (0 <= size <= 4096 and cursor + size <= end):
            raise ValueError('Invalid MapPro structure')
        value = data[cursor:cursor + size].decode('utf-8')
        cursor += size
        return value
    version, next_id, next_info_id = unpack('<iii')
    if not version in (15, 16):
        raise ValueError('Invalid MapPro structure')
    extra = integer() if version == 16 else None
    info_count = integer()
    if not 0 <= info_count <= 10000:
        raise ValueError('Invalid MapPro structure')
    catalog = []
    for _ in range(info_count):
        item_id = integer()
        guid, asset_path = (string(), string())
        decal, server = unpack('<??')
        distances = unpack('<6f')
        catalog.append(dict(id=item_id, guid=guid, path=asset_path, decal=decal, server=server, distances=distances))
    root_count = integer()
    roots = [dict(zip(('x', 'z', 'block'), unpack('<iii'))) for _ in range(root_count)]
    if not cursor == end:
        raise ValueError((cursor, end))
    if not all((0 < root['block'] < count - 1 for root in roots)):
        raise ValueError('Invalid MapPro structure')
    offsets = []
    block_offset = 8 + table_bytes
    for length in lengths:
        offsets.append(block_offset)
        block_offset += length
    info_ids = {item['id'] for item in catalog}
    instances = []
    visited = set()

    def read_cell(block):
        if not (block not in visited and 0 < block < count - 1):
            raise ValueError('Invalid MapPro structure')
        visited.add(block)
        nonlocal cursor, end
        cursor, end = (offsets[block], offsets[block] + lengths[block])
        item_count = integer()
        if not 0 <= item_count <= 1000000:
            raise ValueError('Invalid MapPro structure')
        for _ in range(item_count):
            instance_id = integer() if version == 16 else None
            transform = unpack('<9f')
            info_id, payload = unpack('<ii')
            if not info_id in info_ids:
                raise ValueError('Invalid MapPro structure')
            if not (payload == -1 or 0 <= payload < count - 1):
                raise ValueError('Invalid MapPro structure')
            line = []
            if payload > 0:
                line_count = unpack('<B')[0]
                line = unpack('<' + 'f' * (line_count * 3))
            instances.append(dict(id=instance_id, resource=info_id, position=transform[:3], scale=transform[3:6], rotation=transform[6:], payload=payload, line=line))
        child_count = integer()
        if not 0 <= child_count <= 4:
            raise ValueError('Invalid MapPro structure')
        children = [unpack('<iii') for _ in range(child_count)]
        if not cursor == end:
            raise ValueError((block, cursor, end))
        for _, _, child in children:
            if child:
                read_cell(child)
    for root in roots:
        read_cell(root['block'])
    return dict(source=str(path), size=len(data), version=version, blocks=count, next_id=next_id, next_info_id=next_info_id, extra_v16=extra, catalog=catalog, roots=roots, instances=instances)

if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description='Inspect and validate MapPro v15/v16 maps')
    parser.add_argument('map', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    report = read_catalog(args.map)
    if args.output:
        if args.output.resolve() == args.map.resolve():
            raise ValueError('Output must not overwrite the input map')
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'version': report['version'], 'blocks': report['blocks'],
                      'resources': len(report['catalog']), 'roots': len(report['roots']),
                      'instances': len(report['instances'])}, indent=2))
