import argparse
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dll', type=Path, default=ROOT / 'bin/Release/LegacyMapExpansion.dll')
    args = parser.parse_args()
    if not args.dll.is_file() or args.dll.stat().st_size < 100000000:
        raise ValueError('Build the plugin with embedded resources before packaging.')
    destination = ROOT / 'dist'
    destination.mkdir(exist_ok=True)
    output = destination / 'LegacyMapExpansion-0.2.1.zip'
    with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.write(args.dll, 'LegacyMapExpansion/LegacyMapExpansion.dll')
        archive.write(ROOT / 'docs/INSTALL.md', 'README.md')
    print(output)
