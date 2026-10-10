#!/usr/bin/env python3
"""Prepare and push a release tag. Run from a clean main branch."""
import argparse
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def parse_args():
    parser = argparse.ArgumentParser(
        description='Prepare a release commit and tag, then queue the GitHub release workflow.')
    parser.add_argument('version', help='Semantic version such as 1.3.0')
    notes = parser.add_mutually_exclusive_group()
    notes.add_argument('-n', '--notes', help='Short, user-facing release note text')
    notes.add_argument('--notes-file', type=Path, help='Read Markdown release notes from this file')
    return parser.parse_args()


def main():
    args = parse_args()
    if not re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)', args.version):
        sys.exit('Version must use MAJOR.MINOR.PATCH, for example 1.3.0.')
    version = args.version
    tag = 'v' + version
    if args.notes_file:
        try:
            release_notes = args.notes_file.read_text().strip()
        except OSError as ex:
            sys.exit(f'Could not read release notes: {ex}')
    else:
        release_notes = (args.notes or '').strip()
    if (args.notes is not None or args.notes_file is not None) and not release_notes:
        sys.exit('Release notes cannot be empty.')
    if git('branch', '--show-current') != 'main' or git('status', '--porcelain'):
        sys.exit('Commit your changes and switch to main before releasing.')
    git('fetch', 'origin', 'main', '--tags')
    if git('rev-list', '--count', 'HEAD..origin/main') != '0':
        sys.exit('Local main is behind/diverged from origin/main. Integrate remote changes first.')
    tag_exists = bool(git('tag', '--list', tag))
    if tag_exists and git('rev-list', '-n', '1', tag) != git('rev-parse', 'HEAD'):
        sys.exit('Tag already exists on a different commit; choose a new version.')
    if tag_exists and release_notes:
        existing_notes = git('for-each-ref', 'refs/tags/' + tag, '--format=%(contents)').strip()
        if existing_notes != release_notes:
            sys.exit('Tag already exists with different release notes. Delete the unpushed local tag or reuse its notes.')
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
    if not tag_exists:
        git('tag', '-a', tag, '-m', release_notes or 'Release ' + tag)
    try:
        git('push', '--atomic', 'origin', 'HEAD:refs/heads/main', 'refs/tags/' + tag)
    except subprocess.CalledProcessError:
        sys.exit('Push failed; the local commit/tag are retained. After resolving access or conflicts, retry: '
                 'git push --atomic origin HEAD:refs/heads/main refs/tags/' + tag)
    print('Release queued. Watch the Release workflow in GitHub Actions.')


if __name__ == '__main__':
    main()
