import io
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import urllib.error
import urllib.request
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_existing


def package_bytes(content, signed=False):
    output = io.BytesIO()
    with zipfile.ZipFile(output, 'w') as archive:
        archive.writestr('lib/net10.0/Library.dll', content)
        if signed:
            archive.writestr('.signature.p7s', b'repository signature')
    return output.getvalue()


class ExistingPackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'packages').mkdir()
        self.policy = {'packages': ['Knightfall.Envoy.ControlPlane'], 'version': '1.0.0-rc.1'}
        (self.root / 'packages/Knightfall.Envoy.ControlPlane.1.0.0-rc.1.nupkg').write_bytes(package_bytes(b'verified assembly'))
        self.token = mock.patch.dict(os.environ, {'GH_TOKEN': 'fixture-token'})
        self.token.start()
        self.addCleanup(self.token.stop)

    def response(self, content):
        def fetch(url, authorization, limit, allow_missing=False):
            if url.endswith('index.json'):
                base = url.rsplit('/', 1)[0] + '/content/'
                return json.dumps({'resources': [{'@type': 'PackageBaseAddress/3.0.0', '@id': base}]}).encode()
            return content
        return fetch

    def test_matching_partial_retry_accepts_repository_signed_payload(self):
        with mock.patch.object(check_existing, 'fetch', side_effect=self.response(package_bytes(b'verified assembly', signed=True))):
            matched = check_existing.check_existing(self.root, self.policy)
            self.assertEqual(matched, {'NuGet': self.policy['packages'], 'GitHub': self.policy['packages']})

    def test_conflicting_existing_version_aborts_before_other_registry(self):
        with mock.patch.object(check_existing, 'fetch', side_effect=self.response(package_bytes(b'conflicting assembly'))) as fetch:
            with self.assertRaisesRegex(ValueError, 'Existing NuGet package conflicts'):
                check_existing.check_existing(self.root, self.policy)
            self.assertFalse(any('nuget.pkg.github.com' in call.args[0] for call in fetch.call_args_list))

    def test_missing_versions_are_available_for_first_publication(self):
        with mock.patch.object(check_existing, 'fetch', side_effect=self.response(None)):
            self.assertEqual(check_existing.check_existing(self.root, self.policy), {'NuGet': [], 'GitHub': []})

    def test_registry_content_resource_cannot_redirect_credentials_to_another_host(self):
        index = json.dumps({'resources': [{'@type': 'PackageBaseAddress/3.0.0', '@id': 'https://attacker.invalid/'}]}).encode()
        with mock.patch.object(check_existing, 'fetch', return_value=index) as fetch:
            with self.assertRaisesRegex(ValueError, 'Unexpected registry package-content resource'):
                check_existing.check_existing(self.root, self.policy)
            self.assertFalse(any('attacker.invalid' in call.args[0] for call in fetch.call_args_list))

    def test_redirect_strips_cross_host_authorization_and_rejects_http(self):
        request = urllib.request.Request('https://nuget.pkg.github.com/knightfall/download/package',
                                         headers={'Authorization': 'Basic fixture'})
        handler = check_existing.RegistryRedirects()
        redirected = handler.redirect_request(request, None, 302, '', {}, 'https://storage.example.test/package')
        self.assertFalse(redirected.has_header('Authorization'))
        with self.assertRaisesRegex(ValueError, 'must use HTTPS'):
            handler.redirect_request(request, None, 302, '', {}, 'http://storage.example.test/package')

    def test_only_a_package_404_is_missing_while_authentication_errors_fail(self):
        url = 'https://nuget.pkg.github.com/knightfall/download/package'
        for status in (404, 401):
            with self.subTest(status=status), mock.patch.object(urllib.request, 'build_opener') as opener:
                opener.return_value.open.side_effect = urllib.error.HTTPError(url, status, 'fixture', {}, None)
                if status == 404:
                    self.assertIsNone(check_existing.fetch(url, 'Basic fixture', 1024, allow_missing=True))
                else:
                    with self.assertRaises(urllib.error.HTTPError):
                        check_existing.fetch(url, 'Basic fixture', 1024, allow_missing=True)

    @unittest.skipUnless(shutil.which('pwsh') and os.name != 'nt', 'Requires PowerShell and POSIX command fixtures')
    def test_publisher_skips_only_verified_duplicates_and_stops_on_push_failure(self):
        scripts = Path(__file__).resolve().parents[1]
        first_package = json.loads((scripts / 'policy.json').read_text())['packages'][0]
        for matched in (True, False):
            with self.subTest(matched=matched), tempfile.TemporaryDirectory() as directory:
                directory = Path(directory)
                commands = directory / 'commands'
                commands.mkdir()
                capture = directory / 'push-arguments.txt'
                result = json.dumps({'NuGet': [first_package] if matched else [], 'GitHub': []})
                (commands / 'python3').write_text('#!/bin/sh\nprintf %s ' + shlex.quote(result) + ' > "$3"\n')
                (commands / 'dotnet').write_text('#!/bin/sh\nprintf "%s\\n" "$@" >> "$PUBLISH_ARGS"\nexit 88\n')
                for path in commands.iterdir():
                    path.chmod(0o700)
                environment = dict(os.environ, PATH=str(commands) + os.pathsep + os.environ['PATH'],
                                   RUNNER_TEMP=str(directory), PUBLISH_ARGS=str(capture),
                                   NUGET_API_KEY='fixture-key', GH_TOKEN='fixture-token')
                completed = subprocess.run([shutil.which('pwsh'), '-NoProfile', '-File', str(scripts / 'publish.ps1'),
                                            '-ReleaseDirectory', str(self.root)], env=environment,
                                           capture_output=True, text=True, timeout=30)
                self.assertNotEqual(completed.returncode, 0)
                self.assertIn('NuGet publication failed', completed.stderr)
                arguments = capture.read_text().splitlines()
                self.assertEqual(arguments.count('push'), 1)
                self.assertEqual('--skip-duplicate' in arguments, matched)
                self.assertNotIn('https://nuget.pkg.github.com/knightfall/index.json', arguments)
