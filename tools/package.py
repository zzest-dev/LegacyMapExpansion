import argparse
import json
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dll', type=Path)
    parser.add_argument('--platform', choices=('Windows', 'Linux'), default='Windows')
    args = parser.parse_args()
    if args.dll is None:
        args.dll = ROOT / ('bin/Release/Linux/LegacyMapExpansion.dll' if args.platform == 'Linux' else 'bin/Release/LegacyMapExpansion.dll')
    if not args.dll.is_file() or args.dll.stat().st_size < 100000000:
        raise ValueError('Build the plugin with embedded resources before packaging.')
    destination = ROOT / 'dist'
    destination.mkdir(exist_ok=True)
    version = json.loads((ROOT / 'resources.lock.json').read_text(encoding='utf-8'))['version']
    suffix = '-Linux' if args.platform == 'Linux' else ''
    output = destination / f'LegacyMapExpansion-{version}{suffix}.zip'
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.write(args.dll, 'BepInEx/plugins/LegacyMapExpansion/LegacyMapExpansion.dll')
        archive.write(ROOT / ('docs/INSTALL_LINUX.md' if args.platform == 'Linux' else 'docs/INSTALL.md'), 'BepInEx/plugins/LegacyMapExpansion/README.md')
    print(output)
