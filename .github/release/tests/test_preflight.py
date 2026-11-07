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
            requests = directory / 'requests.jsonl'
            gh = directory / 'gh'
            gh.write_text(f'''#!{sys.executable}
import json
import os
from pathlib import Path
import sys

repository = 'repos/knightfall/envoy-dotnet-control-plane'
expected_tag = repository + '/git/ref/tags/v1.0.0-rc.1'
expected_checks = repository + '/commits/' + os.environ['FIXTURE_SOURCE_SHA'] + '/check-runs?per_page=100'
with Path(os.environ['FIXTURE_REQUESTS']).open('a') as output:
    output.write(json.dumps(sys.argv[1:]) + '\\n')
if sys.argv[1:] == ['api', expected_tag]:
    print(json.dumps({{'object': {{'type': 'commit', 'sha': os.environ['FIXTURE_TAG_SHA']}}}}))
elif sys.argv[1:] == ['api', expected_checks]:
    print(Path(os.environ['FIXTURE_CHECKS']).read_text())
else:
    raise SystemExit('Unexpected GitHub API request: ' + repr(sys.argv[1:]))
''')
            gh.chmod(0o700)
            commit = self.fixture['provenance']['commit']
            environment = dict(os.environ, PATH=str(directory) + os.pathsep + os.environ['PATH'],
                               GITHUB_SHA=commit, FIXTURE_SOURCE_SHA=commit, FIXTURE_TAG_SHA=tag or commit,
                               FIXTURE_CHECKS=str(response), FIXTURE_REQUESTS=str(requests))
            result = subprocess.run([shutil.which('pwsh'), '-NoProfile', '-File', str(self.scripts / 'preflight.ps1')],
                                    env=environment, capture_output=True, text=True, timeout=30)
            return result, [json.loads(line) for line in requests.read_text().splitlines()]

    def test_recorded_main_checks_pass_without_the_pr_only_codeql_aggregate(self):
        result, requests = self.run_preflight(self.fixture)
        self.assertEqual(result.returncode, 0, result.stderr)
        repository = 'repos/knightfall/envoy-dotnet-control-plane'
        self.assertEqual(requests, [
            ['api', repository + '/git/ref/tags/v1.0.0-rc.1'],
            ['api', repository + '/commits/' + self.fixture['provenance']['commit'] + '/check-runs?per_page=100']])

    def test_each_failed_main_analysis_is_rejected_even_with_a_successful_pr_aggregate(self):
        for name in ('Analyze (actions)', 'Analyze (csharp)', 'Analyze (go)'):
            with self.subTest(name=name):
                checks = copy.deepcopy(self.fixture)
                next(check for check in checks['check_runs'] if check['name'] == name)['conclusion'] = 'failure'
                checks['check_runs'].append({'name': 'CodeQL', 'status': 'completed', 'conclusion': 'success',
                                             'app': {'slug': 'github-advanced-security'}})
                result, _ = self.run_preflight(checks)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(name, result.stderr)

    def test_analysis_from_another_application_cannot_satisfy_the_release_gate(self):
        checks = copy.deepcopy(self.fixture)
        next(check for check in checks['check_runs'] if check['name'] == 'Analyze (csharp)')['app']['slug'] = 'fixture-app'
        result, _ = self.run_preflight(checks)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Analyze (csharp)', result.stderr)

    def test_release_tag_must_match_the_exact_source_commit(self):
        result, requests = self.run_preflight(self.fixture, tag='a' * 40)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('lightweight release tag', result.stderr)
        self.assertEqual(len(requests), 1)
