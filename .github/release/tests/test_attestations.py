import importlib.util
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('verify_attestations', Path(__file__).resolve().parents[1] / 'verify-attestations.py')
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class AttestationCorrelationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.package = Path(self.temp.name) / 'package.nupkg'
        self.package.write_bytes(b'verified package')
        self.predicate = {'spdxVersion': 'SPDX-2.2', 'packages': [{'name': 'Cel', 'versionInfo': '0.3.3'}]}
        self.statement = {'predicateType': VERIFY.SPDX, 'predicate': self.predicate,
                          'subject': [{'name': self.package.name, 'digest': {'sha256': VERIFY.digest(self.package)}}]}

    def test_matching_verified_sbom_and_subject_are_accepted(self):
        VERIFY.correlate(self.statement, VERIFY.SPDX, [self.package], self.predicate)

    def test_successful_signature_for_other_sbom_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'intended SBOM data'):
            VERIFY.correlate(self.statement, VERIFY.SPDX, [self.package], {'spdxVersion': 'SPDX-2.2', 'packages': []})

    def test_wrong_subject_digest_is_rejected(self):
        self.statement['subject'][0]['digest']['sha256'] = '0' * 64
        with self.assertRaisesRegex(ValueError, 'intended artifact bytes'):
            VERIFY.correlate(self.statement, VERIFY.SPDX, [self.package], self.predicate)

    def test_missing_or_duplicate_subjects_are_rejected(self):
        for subjects in ([], self.statement['subject'] * 2):
            with self.subTest(subjects=subjects):
                self.statement['subject'] = subjects
                with self.assertRaises(ValueError):
                    VERIFY.correlate(self.statement, VERIFY.SPDX, [self.package], self.predicate)

    def test_provenance_cannot_substitute_for_sbom(self):
        self.statement['predicateType'] = VERIFY.PROVENANCE
        with self.assertRaisesRegex(ValueError, 'predicate type differs'):
            VERIFY.correlate(self.statement, VERIFY.SPDX, [self.package], self.predicate)
