import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SPEC = importlib.util.spec_from_file_location('release_verify', Path(__file__).resolve().parents[1] / 'verify.py')
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class ReleaseVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.policy = {'repository': 'knightfall/envoy-dotnet-control-plane', 'version': '1.0.0-rc.1',
                       'packages': ['Knightfall.Envoy.ControlPlane']}
        self.commit = 'a' * 40
        (self.root / 'packages').mkdir()
        for suffix in ('.nupkg', '.snupkg'):
            self.package(suffix)
        (self.root / '_manifest/spdx_2.2').mkdir(parents=True)
        (self.root / '_manifest/spdx_2.2/manifest.spdx.json').write_text('{}')
        (self.root / 'source-inputs.spdx.json').write_text('{}')
        self.manifest()

    def package(self, suffix, commit=None):
        path = self.root / ('packages/Knightfall.Envoy.ControlPlane.1.0.0-rc.1' + suffix)
        with zipfile.ZipFile(path, 'w') as archive:
            archive.writestr('security/provenance.json', json.dumps({'version': self.policy['version'],
                             'workflow': '.github/workflows/publish.yaml', 'sourceRef': 'refs/heads/main'}))
            archive.writestr('package.nuspec', '<package><metadata><id>Knightfall.Envoy.ControlPlane</id>'
                             '<version>1.0.0-rc.1</version><repository url="https://github.com/'
                             + self.policy['repository'] + '" commit="' + (commit or self.commit)
                             + '"/></metadata></package>')

    def manifest(self):
        files = {path.relative_to(self.root).as_posix(): VERIFY.sha256(path)
                 for path in self.root.rglob('*') if path.is_file() and path.name != 'release.json'}
        value = {'schemaVersion': 1, 'repository': self.policy['repository'],
                 'version': self.policy['version'], 'commit': self.commit, 'files': files}
        (self.root / 'release.json').write_text(json.dumps(value))
        self.digest = VERIFY.sha256(self.root / 'release.json')

    def verify(self):
        return VERIFY.verify_release(self.root, self.digest, self.commit, self.policy)

    def test_complete_source_bound_release_passes(self):
        self.assertEqual(self.verify()['commit'], self.commit)

    def test_artifact_modified_after_manifest_is_rejected(self):
        (self.root / 'source-inputs.spdx.json').write_text('changed')
        with self.assertRaisesRegex(ValueError, 'digest differs'):
            self.verify()

    def test_manifest_modified_after_job_handoff_is_rejected(self):
        (self.root / 'release.json').write_text('{}')
        with self.assertRaisesRegex(ValueError, 'manifest digest differs'):
            self.verify()

    def test_extra_or_missing_files_are_rejected(self):
        for mutation in ('extra', 'missing'):
            with self.subTest(mutation=mutation):
                path = self.root / 'unexpected'
                if mutation == 'extra':
                    path.write_text('not declared')
                else:
                    path.unlink()
                    (self.root / 'source-inputs.spdx.json').unlink()
                with self.assertRaisesRegex(ValueError, 'missing or unmanifested'):
                    self.verify()

    def test_wrong_package_commit_is_rejected_even_with_matching_manifest(self):
        self.package('.nupkg', 'b' * 40)
        self.manifest()
        with self.assertRaisesRegex(ValueError, 'source commit differs'):
            self.verify()

    def test_unexpected_package_is_rejected_even_with_matching_manifest(self):
        (self.root / 'packages/injected.nupkg').write_text('unexpected')
        self.manifest()
        with self.assertRaisesRegex(ValueError, 'package set differs'):
            self.verify()

    def test_publish_context_requires_canonical_main_manual_opt_in(self):
        good = {'GITHUB_REPOSITORY': self.policy['repository'], 'GITHUB_EVENT_NAME': 'workflow_dispatch',
                'GITHUB_REF': 'refs/heads/main', 'RELEASE_PUBLISH': 'true'}
        VERIFY.verify_context(good)
        for key, values in {'GITHUB_REPOSITORY': ['attacker/fork'], 'GITHUB_EVENT_NAME': ['pull_request', 'pull_request_target', 'workflow_run', 'push'],
                            'GITHUB_REF': ['refs/pull/1/merge', 'refs/heads/feature', 'refs/tags/v1.0.0-rc.1'],
                            'RELEASE_PUBLISH': ['false', '']}.items():
            for value in values:
                with self.subTest(key=key, value=value):
                    with self.assertRaises(ValueError):
                        VERIFY.verify_context({**good, key: value})

    def test_symlink_is_rejected(self):
        (self.root / 'link').symlink_to(self.root / 'source-inputs.spdx.json')
        with self.assertRaisesRegex(ValueError, 'Symbolic links'):
            self.verify()


if __name__ == '__main__':
    unittest.main()


class PackagePayloadTests(unittest.TestCase):
    def test_repository_signature_does_not_change_payload_comparison(self):
        spec = importlib.util.spec_from_file_location('compare_packages', Path(__file__).resolve().parents[1] / 'compare-packages.py')
        compare = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(compare)
        with tempfile.TemporaryDirectory() as root:
            original = Path(root) / 'original.nupkg'
            signed = Path(root) / 'signed.nupkg'
            changed = Path(root) / 'changed.nupkg'
            for path, data, signature in [(original, b'original assembly', False), (signed, b'original assembly', True), (changed, b'changed assembly', True)]:
                with zipfile.ZipFile(path, 'w') as archive:
                    archive.writestr('lib/net10.0/Library.dll', data)
                    if signature:
                        archive.writestr('.signature.p7s', b'repository signature')
            self.assertEqual(compare.payload(original), compare.payload(signed))
            self.assertNotEqual(compare.payload(original), compare.payload(changed))
