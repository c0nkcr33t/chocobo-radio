#!/usr/bin/env python3
"""Download a checksum-pinned Dalamud SDK, build, and validate the release package."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
REVISION = '3e8e6eb456c928401febd1d9452c4b8d4cad0eb5'
SDK_SHA256 = '560d283b63d5d70dd5fa7eeb79e7a9cb5139b3dcc63e0cb01b466a57b020fcaa'


def run(*args):
    subprocess.run(args, cwd=ROOT, check=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--tag', default='')
    parser.add_argument('--sdk-archive', type=Path)
    args = parser.parse_args()
    project, = ROOT.glob('*.csproj')
    version = ET.parse(project).findtext('./PropertyGroup/Version')
    if not re.fullmatch(r'\d+\.\d+\.\d+\.0', version or ''):
        raise ValueError('Project version must be major.minor.patch.0')
    if args.tag and args.tag != 'v' + version.removesuffix('.0'):
        raise ValueError('Tag must match project version')
    with tempfile.TemporaryDirectory(prefix='dalamud-build-') as temporary:
        temp = Path(temporary)
        archive = args.sdk_archive or temp / 'dalamud.zip'
        if not args.sdk_archive:
            urllib.request.urlretrieve(f'https://raw.githubusercontent.com/goatcorp/dalamud-distrib/{REVISION}/latest.zip', archive)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != SDK_SHA256:
            raise ValueError('Dalamud archive checksum mismatch')
        sdk = temp / 'sdk'
        with zipfile.ZipFile(archive) as package:
            package.extractall(sdk)
        if not (sdk / 'Dalamud.dll').exists():
            raise ValueError('Dalamud SDK missing assembly')
        hint = '-p:DalamudLibPath=' + str(sdk) + os.sep
        run('dotnet', 'restore', str(project), '--locked-mode', hint)
        run('dotnet', 'build', str(project), '-c', 'Release', '--no-restore', hint)
        for test in sorted((ROOT / 'tests').glob('*.csproj')):
            run('dotnet', 'run', '--project', str(test), '-c', 'Release')
    name = project.stem
    output = ROOT / 'bin' / 'Release' / name / 'latest.zip'
    with zipfile.ZipFile(output) as package:
        manifest = json.loads(package.read(name + '.json'))
        assert name + '.dll' in package.namelist(), 'Plugin DLL missing'
        assert manifest['InternalName'] == name, 'InternalName mismatch'
        assert manifest['AssemblyVersion'] == version, 'Manifest version mismatch'
        assert manifest['DalamudApiLevel'] == 15, 'Pinned SDK requires API 15'
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a') as stream:
            stream.write('asset=' + str(output.relative_to(ROOT)) + '\n')
    print('Validated package:', output)


if __name__ == '__main__':
    main()
