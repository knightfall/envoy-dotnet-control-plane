"""Verify signed bundles with gh, then correlate subjects and SBOM predicates."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import sys

PROVENANCE = 'https://slsa.dev/provenance/v1'
SPDX = 'https://spdx.dev/Document/v2.2'


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''):
            result.update(chunk)
    return result.hexdigest()


def correlate(statement, predicate_type, expected_paths, expected_predicate=None):
    if statement.get('predicateType') != predicate_type:
        raise ValueError('Verified attestation predicate type differs')
    expected = {path.name: digest(path) for path in expected_paths}
    subjects = statement.get('subject', [])
    if len(subjects) != len(expected):
        raise ValueError('Verified attestation subject set differs')
    observed = {}
    for subject in subjects:
        name = PurePosixPath(subject['name']).name
        if name in observed:
            raise ValueError('Duplicate verified attestation subject')
        observed[name] = subject.get('digest', {}).get('sha256')
    if observed != expected:
        raise ValueError('Verified attestation does not bind the intended artifact bytes')
    if expected_predicate is not None and statement.get('predicate') != expected_predicate:
        raise ValueError('Verified attestation does not contain the intended SBOM data')


def verify(root, repository, commit, bundles):
    root = Path(root)
    packages = sorted((root / 'packages').iterdir())
    sboms = [root / '_manifest/spdx_2.2/manifest.spdx.json', root / 'source-inputs.spdx.json']
    cases = [(bundles[0], PROVENANCE, packages + [root / 'release.json', *sboms], None),
             (bundles[1], SPDX, packages, json.loads(sboms[0].read_text())),
             (bundles[2], SPDX, packages, json.loads(sboms[1].read_text()))]
    for bundle, predicate_type, subjects, predicate in cases:
        output = subprocess.run(['gh', 'attestation', 'verify', str(packages[0]), '--bundle', bundle,
                                 '--repo', repository, '--signer-workflow', repository + '/.github/workflows/publish.yaml',
                                 '--source-ref', 'refs/heads/main', '--source-digest', commit,
                                 '--deny-self-hosted-runners', '--predicate-type', predicate_type, '--format', 'json'],
                                check=True, capture_output=True, text=True)
        results = json.loads(output.stdout)
        if len(results) != 1:
            raise ValueError('Expected exactly one verified attestation per bundle')
        correlate(results[0]['verificationResult']['statement'], predicate_type, subjects, predicate)
    print('Verified provenance and both SBOM signatures, source identities, complete subject sets and exact SBOM data.')


if __name__ == '__main__':
    verify(sys.argv[1], os.environ['GITHUB_REPOSITORY'], os.environ['GITHUB_SHA'],
           [os.environ['PROVENANCE_BUNDLE'], os.environ['BUILD_SBOM_BUNDLE'], os.environ['SOURCE_SBOM_BUNDLE']])
