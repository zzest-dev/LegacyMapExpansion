import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def verify(include_inputs=True, platform='Windows'):
    lock = json.loads((ROOT / 'resources.lock.json').read_text(encoding='utf-8'))
    if platform == 'Linux' and 'Resources/Linux/map1018.bundle' not in lock['files']:
        raise ValueError('Linux bundle is not recorded in resources.lock.json.')
    for name, expected in lock['files'].items():
        if name.startswith('Resources/Linux/') and platform != 'Linux':
            continue
        if name == 'Resources/map1018.bundle' and platform == 'Linux':
            continue
        if not include_inputs and name.startswith('PortingInputs/'):
            continue
        path = ROOT / name
        if not path.is_file():
            raise ValueError(f'Missing {name}. Run git lfs pull.')
        with path.open('rb') as stream:
            if stream.read(80).startswith(b'version https://git-lfs.github.com/spec/v1'):
                raise ValueError(f'{name} is an LFS pointer. Run git lfs pull.')
        if path.stat().st_size != expected['bytes'] or digest(path) != expected['sha256']:
            raise ValueError(f'Resource differs from resources.lock.json: {name}')
        print(f'OK {name}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--runtime-only', action='store_true')
    parser.add_argument('--platform', choices=('Windows', 'Linux'), default='Windows')
    args = parser.parse_args()
    verify(not args.runtime_only, args.platform)
