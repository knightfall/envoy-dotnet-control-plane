"""Verify the original production candidate without rebuilding or publishing it."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import zipfile

from verify import require, sha256, verify_release

SPEC = importlib.util.spec_from_file_location('verify_attestations', Path(__file__).with_name('verify-attestations.py'))
ATTESTATIONS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ATTESTATIONS)


def verify_candidate(root, environment):
    policy = json.loads(Path(__file__).with_name('policy.json').read_text())
    repository = policy['repository']
    commit = environment['RELEASE_SOURCE_COMMIT']
    require(environment.get('GITHUB_REPOSITORY') == repository, 'Wrong release repository')
    require(environment.get('GITHUB_EVENT_NAME') == 'workflow_dispatch', 'Recovery requires manual dispatch')
    require(environment.get('GITHUB_REF') == 'refs/heads/main', 'Recovery requires main')
    tag_result = subprocess.run(['gh', 'api', f'repos/{repository}/git/ref/tags/v{policy["version"]}'],
                                check=True, capture_output=True, text=True)
    tag = json.loads(tag_result.stdout)['object']
    require(tag['type'] == 'commit' and tag['sha'] == commit, 'Protected release tag differs')
    verify_release(root, environment['RELEASE_MANIFEST_SHA256'], commit, policy)
    subprocess.run(['gh', 'attestation', 'verify', str(Path(root) / 'release.json'),
                    '--repo', repository, '--signer-workflow', repository + '/.github/workflows/publish.yaml',
                    '--source-ref', 'refs/heads/main', '--source-digest', commit,
                    '--deny-self-hosted-runners', '--predicate-type', 'https://slsa.dev/provenance/v1'], check=True)


def verify_assets(root, assets, repository, commit):
    root, assets = Path(root), Path(assets)
    expected = {path.name: path for path in (root / 'packages').iterdir()}
    expected.update({'release.json': root / 'release.json', 'source-inputs.spdx.json': root / 'source-inputs.spdx.json',
                     'build.spdx.json': root / '_manifest/spdx_2.2/manifest.spdx.json'})
    bundles = []
    for prefix in ('provenance', 'build-sbom', 'source-sbom'):
        matching = list(assets.glob(prefix + '-*.sigstore.json'))
        require(len(matching) == 1, 'Missing or ambiguous release attestation: ' + prefix)
        bundle = matching[0]
        require(bundle.name == f'{prefix}-{sha256(bundle)}.sigstore.json', 'Release bundle digest name differs')
        bundles.append(str(bundle))
    actual = {path.name for path in assets.iterdir()}
    require(actual == {*expected, 'release-evidence.zip', *(Path(bundle).name for bundle in bundles)},
            'Release asset set differs')
    for name, original in expected.items():
        require(sha256(assets / name) == sha256(original), 'Release asset differs: ' + name)
    manifest = json.loads((root / 'release.json').read_text())
    files = dict(manifest['files'], **{'release.json': sha256(root / 'release.json')})
    with zipfile.ZipFile(assets / 'release-evidence.zip') as archive:
        names = archive.namelist()
        require(len(names) == len(files) and set(names) == set(files), 'Release evidence file set differs')
        for name, digest in files.items():
            require(hashlib.sha256(archive.read(name)).hexdigest() == digest, 'Release evidence differs: ' + name)
    ATTESTATIONS.verify(root, repository, commit, bundles)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--assets', type=Path, help='Also verify the downloaded draft release assets before announcement')
    arguments = parser.parse_args()
    verify_candidate(arguments.candidate, os.environ)
    print('Verified the protected tag, signed manifest and complete original candidate.')
    if arguments.assets is not None:
        verify_assets(arguments.candidate, arguments.assets, os.environ['GITHUB_REPOSITORY'], os.environ['RELEASE_SOURCE_COMMIT'])
        print('Verified every release asset, the complete evidence ZIP and all three signed bundles.')
