"""Check package README repository links against the candidate checkout.

This intentionally supports inline Markdown links and plain ATX headings only.
External links are outside its scope; reference links and HTML anchors in package
READMEs are rejected so they cannot silently evade the check.
"""
import argparse
import json
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit
import zipfile


INLINE_LINK = re.compile(r'!?\[[^\[\]\n]+\]\(([^()\s]+)\)')
FENCE = re.compile(r'^\s{0,3}(`{3,}|~{3,})')
HEADING = re.compile(r'^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$')
PLAIN_HEADING = re.compile(r'^[A-Za-z0-9 ,._-]+$')


def prose_lines(content):
    """Yield lines outside fenced and inline code, preserving line numbers."""
    fence = None
    for number, line in enumerate(content.splitlines(), 1):
        match = FENCE.match(line)
        if match:
            marker = match.group(1)
            if fence is None:
                fence = marker
            elif marker[0] == fence[0] and len(marker) >= len(fence):
                fence = None
            continue
        if fence is None:
            yield number, re.sub(r'(`+).*?\1', '', line)
    if fence is not None:
        raise ValueError('unclosed code fence')


def heading_ids(path):
    counts = {}
    result = set()
    previous = ''
    for _, line in prose_lines(path.read_text(encoding='utf-8')):
        if previous.strip() and re.fullmatch(r'\s{0,3}(?:=+|-+)\s*', line):
            raise ValueError(f'{path}: setext headings are unsupported for fragment checks')
        match = HEADING.match(line)
        if match:
            title = match.group(1)
            if not PLAIN_HEADING.fullmatch(title):
                raise ValueError(f'{path}: unsupported heading syntax: {title}')
            base = title.lower().replace(',', '').replace('.', '').replace(' ', '-')
            count = counts.get(base, 0)
            candidate = base if count == 0 else f'{base}-{count}'
            while candidate in result:
                count += 1
                candidate = f'{base}-{count}'
            counts[base] = count + 1
            result.add(candidate)
        previous = line
    return result


def check_readme(root, readme, repository):
    root = root.resolve()
    readme = readme.resolve()
    for number, line in prose_lines(readme.read_text(encoding='utf-8')):
        links = list(INLINE_LINK.finditer(line))
        remainder = INLINE_LINK.sub('', line)
        if ('[' in remainder or ']' in remainder or re.search(r'<\s*a\b', remainder, re.I)
                or f'<https://github.com/{repository}/blob/main/' in remainder):
            raise ValueError(f'{readme}:{number}: unsupported link syntax')
        for match in links:
            destination = match.group(1)
            parsed = urlsplit(destination)
            if parsed.scheme or parsed.netloc:
                prefix = f'/{repository}/blob/main/'
                if parsed.scheme != 'https' or parsed.netloc.lower() != 'github.com' or not parsed.path.startswith(prefix):
                    continue
                target = root / unquote(parsed.path[len(prefix):])
            else:
                target = readme.parent / unquote(parsed.path) if parsed.path else readme
            if parsed.query:
                raise ValueError(f'{readme}:{number}: query strings are unsupported for repository links: {destination}')
            target = target.resolve()
            if not target.is_relative_to(root) or not target.is_file():
                raise ValueError(f'{readme}:{number}: repository link target is missing or outside the checkout: {destination}')
            if parsed.fragment:
                if target.suffix.lower() != '.md' or unquote(parsed.fragment) not in heading_ids(target):
                    raise ValueError(f'{readme}:{number}: missing or unsupported fragment: {destination}')


def check_package_readmes(root, release=None):
    policy = json.loads((root / '.github/release/policy.json').read_text(encoding='utf-8'))
    for package in policy['packages']:
        if not package.startswith('Knightfall.'):
            raise ValueError(f'Unsupported package name: {package}')
        readme = root / 'src' / package.removeprefix('Knightfall.') / 'README.md'
        check_readme(root, readme, policy['repository'])
        if release is not None:
            archive_path = release / 'packages' / f"{package}.{policy['version']}.nupkg"
            with zipfile.ZipFile(archive_path) as archive:
                if archive.namelist().count('README.md') != 1:
                    raise ValueError(f'{archive_path}: expected exactly one README.md')
                if archive.read('README.md') != readme.read_bytes():
                    raise ValueError(f'{archive_path}: README.md does not match the checked source README')
    return len(policy['packages'])


if __name__ == '__main__':
    try:
        parser = argparse.ArgumentParser(description=__doc__)
        parser.add_argument('--release', type=Path, help='also compare each candidate package README with source')
        args = parser.parse_args()
        root = Path(__file__).resolve().parents[2]
        count = check_package_readmes(root, args.release)
        print(f'Checked repository links in {count} package READMEs against the candidate checkout.')
        if args.release is not None:
            print(f'All {count} candidate package READMEs match their checked source files.')
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        sys.exit(str(error))
