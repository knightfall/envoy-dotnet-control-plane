import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


@unittest.skipUnless(shutil.which('pwsh'), 'Requires PowerShell')
class ConsumerSourceTests(unittest.TestCase):
    def configuration(self, registry):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'release/packages').mkdir(parents=True)
            environment = dict(os.environ, RUNNER_TEMP=str(root), RELEASE_TEST_REGISTRY=registry,
                               RELEASE_TEST_DIRECTORY=str(root / 'release'),
                               RELEASE_TEST_SCRIPT=str(Path(__file__).resolve().parents[1] / 'verify-published.ps1'))
            # Stop at the first restore; inspect the configuration emitted by the real script.
            command = "function dotnet { throw 'CapturedBeforeRestore' }; & $env:RELEASE_TEST_SCRIPT -Registry $env:RELEASE_TEST_REGISTRY -ReleaseDirectory $env:RELEASE_TEST_DIRECTORY"
            result = subprocess.run([shutil.which('pwsh'), '-NoProfile', '-Command', command],
                                    env=environment, capture_output=True, text=True, timeout=30)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('CapturedBeforeRestore', result.stderr)
            return ET.parse(root / f'consumer-{registry}/NuGet.Config').getroot()

    def test_nuget_uses_one_source_with_mapping_for_packages_and_dependencies(self):
        configuration = self.configuration('nuget')
        sources = configuration.findall('packageSources/add')
        self.assertEqual([(source.get('key'), source.get('value')) for source in sources],
                         [('release', 'https://api.nuget.org/v3/index.json')])
        mappings = configuration.findall('packageSourceMapping/packageSource')
        self.assertEqual([(source.get('key'), [p.get('pattern') for p in source]) for source in mappings],
                         [('release', ['*'])])

    def test_other_sources_isolate_own_packages_from_nuget_dependencies(self):
        for registry in ('candidate', 'github'):
            with self.subTest(registry=registry):
                configuration = self.configuration(registry)
                sources = {source.get('key'): source.get('value') for source in configuration.findall('packageSources/add')}
                self.assertEqual(set(sources), {'release', 'dependencies'})
                self.assertNotEqual(sources['release'], sources['dependencies'])
                self.assertEqual(sources['dependencies'], 'https://api.nuget.org/v3/index.json')
                if registry == 'github':
                    self.assertEqual(sources['release'], 'https://nuget.pkg.github.com/knightfall/index.json')
                else:
                    self.assertEqual(Path(sources['release']).parts[-2:], ('release', 'packages'))
                mappings = configuration.findall('packageSourceMapping/packageSource')
                self.assertEqual({source.get('key'): [p.get('pattern') for p in source] for source in mappings},
                                 {'release': ['Knightfall.Envoy.*'], 'dependencies': ['*']})
