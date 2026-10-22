import json
import shutil
import zipfile
from pathlib import Path, PurePosixPath

from verify_resources import ROOT, verify


def prepare():
    verify()
    target = (ROOT / 'UnityProject').resolve()
    protected = {'Editor', 'MapShaders', 'TerrainLayers.shader', 'TerrainLayers.shader.meta'}
    with zipfile.ZipFile(ROOT / 'PortingInputs/UnityAssets.zip') as archive:
        entries = []
        for item in archive.infolist():
            path = PurePosixPath(item.filename)
            if '\\' in item.filename or path.is_absolute() or '..' in path.parts:
                raise ValueError(f'Unsafe archive path: {item.filename}')
            if len(path.parts) < 2 or path.parts[0] != 'Assets' or path.parts[1] in protected:
                raise ValueError(f'Unexpected archive path: {item.filename}')
            destination = target.joinpath(*path.parts).resolve()
            if target not in destination.parents:
                raise ValueError(f'Path leaves UnityProject: {item.filename}')
            entries.append((item, destination))
        for item, destination in entries:
            if item.is_dir():
                destination.mkdir(parents=True, exist_ok=True)
                continue
            destination.parent.mkdir(parents=True, exist_ok=True)
            mode = 'r+b' if destination.exists() else 'wb'
            with archive.open(item) as source, destination.open(mode) as output:
                shutil.copyfileobj(source, output)
                output.truncate()
    regions = json.loads((ROOT / 'porting/regions.json').read_text(encoding='utf-8'))
    (target / 'Assets/regions.json').write_text(json.dumps({'regions': regions}, indent=2), encoding='utf-8')
    print(f'Restored {len(entries)} Unity asset files to {target}')


if __name__ == '__main__':
    prepare()
