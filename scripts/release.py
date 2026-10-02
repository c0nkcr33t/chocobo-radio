#!/usr/bin/env python3
"""Prepare and push a release tag. Run from a clean main branch."""
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def main():
    if len(sys.argv) != 2 or not re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)', sys.argv[1]):
        sys.exit('Usage: ./scripts/release.sh MAJOR.MINOR.PATCH')
    version = sys.argv[1]
    tag = 'v' + version
    if git('branch', '--show-current') != 'main' or git('status', '--porcelain'):
        sys.exit('Commit your changes and switch to main before releasing.')
    git('fetch', 'origin', 'main', '--tags')
    if git('rev-list', '--count', 'HEAD..origin/main') != '0':
        sys.exit('Local main is behind/diverged from origin/main. Integrate remote changes first.')
    if git('tag', '--list', tag):
        sys.exit('Tag already exists; choose a new version.')
    project, = ROOT.glob('*.csproj')
    current = ET.parse(project).findtext('./PropertyGroup/Version')
    if tuple(map(int, (version + '.0').split('.'))) < tuple(map(int, current.split('.'))):
        sys.exit('Version cannot go backward.')
    if current != version + '.0':
        text = project.read_text()
        text, count = re.subn(r'<Version>[^<]+</Version>', '<Version>' + version + '.0</Version>', text, count=1)
        if count != 1:
            sys.exit('Could not find project Version')
        project.write_text(text)
        git('add', project.name)
        git('commit', '-m', 'Prepare release ' + tag)
    git('tag', '-a', tag, '-m', 'Release ' + tag)
    try:
        git('push', '--atomic', 'origin', 'HEAD:refs/heads/main', 'refs/tags/' + tag)
    except subprocess.CalledProcessError:
        sys.exit('Push failed; the local commit/tag are retained. After resolving access or conflicts, retry: '
                 'git push --atomic origin HEAD:refs/heads/main refs/tags/' + tag)
    print('Release queued. Watch the Release workflow in GitHub Actions.')


if __name__ == '__main__':
    main()
