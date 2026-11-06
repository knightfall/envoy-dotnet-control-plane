import copy
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


@unittest.skipUnless(shutil.which('pwsh') and os.name != 'nt', 'Requires PowerShell and POSIX command fixtures')
class MainPreflightTests(unittest.TestCase):
    def setUp(self):
        self.scripts = Path(__file__).resolve().parents[1]
        self.fixture = json.loads((Path(__file__).with_name('fixtures') / 'main-checks.json').read_text())

    def run_preflight(self, checks, tag=None):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            response = directory / 'checks.json'
            response.write_text(json.dumps(checks))
            gh = directory / 'gh'
            gh.write_text(f'''#!{sys.executable}
import json
import os
from pathlib import Path
import sys

if '/git/ref/tags/' in sys.argv[-1]:
    print(json.dumps({{'object': {{'type': 'commit', 'sha': os.environ['FIXTURE_TAG_SHA']}}}}))
else:
    print(Path(os.environ['FIXTURE_CHECKS']).read_text())
''')
            gh.chmod(0o700)
            commit = self.fixture['provenance']['commit']
            environment = dict(os.environ, PATH=str(directory) + os.pathsep + os.environ['PATH'],
                               GITHUB_SHA=commit, FIXTURE_TAG_SHA=tag or commit, FIXTURE_CHECKS=str(response))
            return subprocess.run([shutil.which('pwsh'), '-NoProfile', '-File', str(self.scripts / 'preflight.ps1')],
                                  env=environment, capture_output=True, text=True, timeout=30)

    def test_recorded_main_checks_pass_without_the_pr_only_codeql_aggregate(self):
        result = self.run_preflight(self.fixture)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_each_failed_main_analysis_is_rejected_even_with_a_successful_pr_aggregate(self):
        for name in ('Analyze (actions)', 'Analyze (csharp)', 'Analyze (go)'):
            with self.subTest(name=name):
                checks = copy.deepcopy(self.fixture)
                next(check for check in checks['check_runs'] if check['name'] == name)['conclusion'] = 'failure'
                checks['check_runs'].append({'name': 'CodeQL', 'status': 'completed', 'conclusion': 'success',
                                             'app': {'slug': 'github-advanced-security'}})
                result = self.run_preflight(checks)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(name, result.stderr)

    def test_analysis_from_another_application_cannot_satisfy_the_release_gate(self):
        checks = copy.deepcopy(self.fixture)
        next(check for check in checks['check_runs'] if check['name'] == 'Analyze (csharp)')['app']['slug'] = 'fixture-app'
        result = self.run_preflight(checks)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Analyze (csharp)', result.stderr)

    def test_release_tag_must_match_the_exact_source_commit(self):
        result = self.run_preflight(self.fixture, tag='a' * 40)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('lightweight release tag', result.stderr)
