import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
SPEC = importlib.util.spec_from_file_location('published_candidate', SCRIPTS / 'verify-published-candidate.py')
RECOVERY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RECOVERY)


class PublishedCandidateTests(unittest.TestCase):
    def setUp(self):
        self.commit = 'a' * 40
        self.digest = 'b' * 64
        self.environment = {'GITHUB_REPOSITORY': 'knightfall/envoy-dotnet-control-plane',
                            'GITHUB_EVENT_NAME': 'workflow_dispatch', 'GITHUB_REF': 'refs/heads/main',
                            'RELEASE_SOURCE_COMMIT': self.commit, 'RELEASE_MANIFEST_SHA256': self.digest}

    def tag(self, commit=None, kind='commit'):
        return subprocess.CompletedProcess([], 0, json.dumps({'object': {'type': kind, 'sha': commit or self.commit}}))

    def test_exact_manifest_and_original_source_are_verified_before_execution(self):
        with mock.patch.object(RECOVERY.subprocess, 'run', return_value=self.tag()) as run, \
                mock.patch.object(RECOVERY, 'verify_release') as verify:
            RECOVERY.verify_candidate(Path('/candidate'), self.environment)
        self.assertEqual(verify.call_args.args[:3], (Path('/candidate'), self.digest, self.commit))
        self.assertEqual(verify.call_args.args[3]['version'], '1.0.0-rc.1')
        self.assertEqual([call.args[0] for call in run.call_args_list], [
            ['gh', 'api', 'repos/knightfall/envoy-dotnet-control-plane/git/ref/tags/v1.0.0-rc.1'],
            ['gh', 'attestation', 'verify', '/candidate/release.json',
             '--repo', 'knightfall/envoy-dotnet-control-plane', '--signer-workflow',
             'knightfall/envoy-dotnet-control-plane/.github/workflows/publish.yaml',
             '--source-ref', 'refs/heads/main', '--source-digest', self.commit,
             '--deny-self-hosted-runners', '--predicate-type', 'https://slsa.dev/provenance/v1']])
        self.assertTrue(all(call.kwargs['check'] for call in run.call_args_list))

    def test_forks_pr_events_and_non_main_refs_are_rejected_before_network_access(self):
        for key, value in [('GITHUB_REPOSITORY', 'fork/envoy-dotnet-control-plane'),
                           ('GITHUB_EVENT_NAME', 'pull_request'), ('GITHUB_REF', 'refs/heads/topic')]:
            with self.subTest(key=key), mock.patch.object(RECOVERY.subprocess, 'run') as run:
                with self.assertRaises(ValueError):
                    RECOVERY.verify_candidate('/candidate', dict(self.environment, **{key: value}))
                run.assert_not_called()

    def test_moved_or_annotated_tag_is_rejected_before_candidate_loading(self):
        for tag in (self.tag('c' * 40), self.tag(kind='tag')):
            with self.subTest(tag=tag.stdout), mock.patch.object(RECOVERY.subprocess, 'run', return_value=tag) as run, \
                    mock.patch.object(RECOVERY, 'verify_release') as verify:
                with self.assertRaisesRegex(ValueError, 'Protected release tag differs'):
                    RECOVERY.verify_candidate('/candidate', self.environment)
                self.assertEqual(run.call_count, 1)
                verify.assert_not_called()

    def test_candidate_verification_failure_prevents_attestation_and_consumer_execution(self):
        with mock.patch.object(RECOVERY.subprocess, 'run', return_value=self.tag()) as run, \
                mock.patch.object(RECOVERY, 'verify_release', side_effect=ValueError('Artifact digest differs')):
            with self.assertRaisesRegex(ValueError, 'Artifact digest differs'):
                RECOVERY.verify_candidate('/candidate', self.environment)
            self.assertEqual(run.call_count, 1)

    def test_signature_verification_failure_is_not_ignored(self):
        failure = subprocess.CalledProcessError(1, ['gh', 'attestation', 'verify'])
        with mock.patch.object(RECOVERY.subprocess, 'run', side_effect=[self.tag(), failure]), \
                mock.patch.object(RECOVERY, 'verify_release'):
            with self.assertRaises(subprocess.CalledProcessError) as raised:
                RECOVERY.verify_candidate('/candidate', self.environment)
            self.assertIs(raised.exception, failure)


class PublishedAssetTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / 'candidate'
        self.assets = Path(self.temporary.name) / 'assets'
        self.assets.mkdir()
        files = {'packages/Library.nupkg': b'package', 'packages/Library.snupkg': b'symbols',
                 'source-inputs.spdx.json': b'source SBOM', '_manifest/spdx_2.2/manifest.spdx.json': b'build SBOM',
                 'evidence/.github/release/policy.json': b'protected release policy'}
        for name, data in files.items():
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        manifest = {'files': {name: RECOVERY.sha256(self.root / name) for name in files}}
        (self.root / 'release.json').write_text(json.dumps(manifest))
        for source, target in [('packages/Library.nupkg', 'Library.nupkg'), ('packages/Library.snupkg', 'Library.snupkg'),
                               ('source-inputs.spdx.json', 'source-inputs.spdx.json'),
                               ('_manifest/spdx_2.2/manifest.spdx.json', 'build.spdx.json'), ('release.json', 'release.json')]:
            (self.assets / target).write_bytes((self.root / source).read_bytes())
        self.bundles = []
        for prefix in ('provenance', 'build-sbom', 'source-sbom'):
            data = prefix.encode()
            digest = RECOVERY.hashlib.sha256(data).hexdigest()
            bundle = self.assets / f'{prefix}-{digest}.sigstore.json'
            bundle.write_bytes(data)
            self.bundles.append(str(bundle))
        self.archive()

    def archive(self, omit=None):
        with zipfile.ZipFile(self.assets / 'release-evidence.zip', 'w') as archive:
            for path in self.root.rglob('*'):
                if path.is_file() and path.relative_to(self.root).as_posix() != omit:
                    archive.write(path, path.relative_to(self.root).as_posix())

    def verify(self):
        RECOVERY.verify_assets(self.root, self.assets, 'knightfall/envoy-dotnet-control-plane', 'a' * 40)

    def test_complete_assets_are_compared_and_all_three_bundles_verified(self):
        with mock.patch.object(RECOVERY.ATTESTATIONS, 'verify') as verify:
            self.verify()
        verify.assert_called_once_with(self.root, 'knightfall/envoy-dotnet-control-plane', 'a' * 40, self.bundles)

    def test_changed_package_asset_is_rejected_before_signature_verification(self):
        (self.assets / 'Library.nupkg').write_bytes(b'replacement package')
        with mock.patch.object(RECOVERY.ATTESTATIONS, 'verify') as verify:
            with self.assertRaisesRegex(ValueError, 'Release asset differs: Library.nupkg'):
                self.verify()
            verify.assert_not_called()

    def test_missing_bundle_and_unexpected_asset_are_rejected(self):
        bundle = Path(self.bundles[0])
        bundle.unlink()
        with self.assertRaisesRegex(ValueError, 'Missing or ambiguous release attestation'):
            self.verify()
        bundle.write_bytes(b'provenance')
        (self.assets / 'unexpected').write_bytes(b'extra')
        with self.assertRaisesRegex(ValueError, 'Release asset set differs'):
            self.verify()

    def test_missing_hidden_evidence_is_rejected(self):
        self.archive(omit='evidence/.github/release/policy.json')
        with self.assertRaisesRegex(ValueError, 'Release evidence file set differs'):
            self.verify()

    def test_altered_evidence_payload_is_rejected(self):
        (self.root / 'evidence/.github/release/policy.json').write_bytes(b'changed policy')
        self.archive()
        with self.assertRaisesRegex(ValueError, 'Release evidence differs: evidence/.github/release/policy.json'):
            self.verify()

    def test_bundle_signature_failure_is_not_ignored(self):
        with mock.patch.object(RECOVERY.ATTESTATIONS, 'verify', side_effect=ValueError('Signature rejected')):
            with self.assertRaisesRegex(ValueError, 'Signature rejected'):
                self.verify()
