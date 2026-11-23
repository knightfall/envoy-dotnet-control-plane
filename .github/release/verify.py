"""Verify release data without loading or building candidate code."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import sys
import zipfile
import xml.etree.ElementTree as ET


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def verify_package_readme(readme, policy):
    """Reject versioned release links and package filenames from a different release."""
    repository_url = 'https://github.com/' + policy['repository']
    version = policy['version']
    release_url = re.escape(repository_url) + r'/releases/(?:tag|download)/([^/\s?#)]+)'
    tagged_blob_url = re.escape(repository_url) + r'/blob/(v\d[^/\s?#)]+)/'
    for pattern in (release_url, tagged_blob_url):
        for match in re.finditer(pattern, readme):
            require(match.group(1) == 'v' + version, 'Package README release reference differs: ' + match.group(0))

    package_names = '|'.join(re.escape(name) for name in sorted(policy['packages'], key=len, reverse=True))
    package_file = r'(?<![\w.-])(?:' + package_names + r')\.(\d[^\s/`<>)]*?)\.(?:nupkg|snupkg)\b'
    for match in re.finditer(package_file, readme):
        require(match.group(1) == version, 'Package README package version differs: ' + match.group(0))
    # A README committed in the candidate cannot know the digest of that same commit.
    require(not re.search(r'--source-digest(?:\s+|=)[0-9a-fA-F]{40}\b', readme),
            'Package README contains a fixed source digest')


def verify_release(root, manifest_hash, commit, policy):
    root = Path(root).resolve(strict=True)
    require(sha256(root / 'release.json') == manifest_hash, 'Release manifest digest differs')
    manifest = json.loads((root / 'release.json').read_text())
    require(manifest['schemaVersion'] == 1, 'Unknown release manifest version')
    require(manifest['repository'] == policy['repository'], 'Release repository differs')
    require(manifest['version'] == policy['version'] and manifest['commit'] == commit, 'Release version/commit differs')
    files = manifest['files']
    expected = {'release.json', *files}
    actual = set()
    for path in root.rglob('*'):
        require(not path.is_symlink(), 'Symbolic links are not release artifacts')
        if path.is_file():
            actual.add(path.relative_to(root).as_posix())
    require(actual == expected, 'Release contains missing or unmanifested files')
    for name, digest in files.items():
        path = PurePosixPath(name)
        require(not path.is_absolute() and '..' not in path.parts and '\\' not in name and ':' not in name,
                'Unsafe artifact path')
        require((root / name).resolve().is_relative_to(root), 'Artifact escapes release directory')
        require(sha256(root / name) == digest, 'Artifact digest differs: ' + name)
    packages = {f'packages/{name}.{policy["version"]}{extension}'
                for name in policy['packages'] for extension in ('.nupkg', '.snupkg')}
    require({name for name in files if name.startswith('packages/')} == packages, 'Release package set differs')
    for package in packages:
        name = package.rsplit('/', 1)[1].removesuffix('.snupkg').removesuffix('.nupkg')
        package_id = name.removesuffix('.' + policy['version'])
        with zipfile.ZipFile(root / package) as archive:
            metadata = ET.fromstring(archive.read(next(name for name in archive.namelist() if name.endswith('.nuspec'))))
            fields = {element.tag.rsplit('}', 1)[-1]: element for element in metadata.iter()}
            require(fields['id'].text == package_id and fields['version'].text == policy['version'], 'Package identity differs')
            require(fields['repository'].get('commit') == commit, 'Package source commit differs')
            require(fields['repository'].get('url') == 'https://github.com/' + policy['repository'], 'Package repository differs')
            # SDK symbol packages retain their nuspec/source identity, but omit package content files.
            if package.endswith('.nupkg'):
                require('readme' in fields and fields['readme'].text == 'README.md'
                        and 'README.md' in archive.namelist(), 'Package README is missing')
                verify_package_readme(archive.read('README.md').decode('utf-8'), policy)
                require('security/provenance.json' in archive.namelist(), 'Package verification locator is missing')
                locator = json.loads(archive.read('security/provenance.json'))
                require(locator['version'] == policy['version'] and locator['workflow'] == '.github/workflows/publish.yaml'
                        and locator['sourceRef'] == 'refs/heads/main'
                        and locator['repository'] == 'https://github.com/' + policy['repository']
                        and locator['release'] == 'https://github.com/' + policy['repository']
                        + '/releases/tag/v' + policy['version'], 'Package verification locator differs')
    require('_manifest/spdx_2.2/manifest.spdx.json' in files and 'source-inputs.spdx.json' in files, 'Missing SBOMs')
    return manifest


def verify_context(environment):
    require(environment.get('GITHUB_REPOSITORY') == 'knightfall/envoy-dotnet-control-plane', 'Wrong release repository')
    require(environment.get('GITHUB_EVENT_NAME') == 'workflow_dispatch', 'Publishing requires manual dispatch')
    require(environment.get('GITHUB_REF') == 'refs/heads/main', 'Publishing requires main')
    require(environment.get('RELEASE_PUBLISH') == 'true', 'Publishing was not selected')


if __name__ == '__main__':
    verify_context(os.environ)
    verify_release(sys.argv[1], os.environ['RELEASE_MANIFEST_SHA256'], os.environ['GITHUB_SHA'],
                   json.loads(Path(__file__).with_name('policy.json').read_text()))
    print('Verified release context, complete artifact set, SBOMs and source-bound packages.')
