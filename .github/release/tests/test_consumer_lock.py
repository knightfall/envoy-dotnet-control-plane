import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location('consumer_lock', Path(__file__).resolve().parents[1] / 'verify-consumer-lock.py')
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)


class ConsumerLockTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.policy = {'version': '1.0.0-rc.3', 'packages': ['Example.Library']}
        self.consumer = self.root / 'consumer.lock.json'
        self.trusted = self.root / 'evidence/src/Example.Library/packages.lock.json'
        self.trusted.parent.mkdir(parents=True)
        self.write_lock(self.trusted, {'Example.Dependency': self.package('2.1.0', 'sealed dependency hash')})

    @staticmethod
    def package(version, content_hash):
        return {'type': 'Transitive', 'resolved': version, 'contentHash': content_hash}

    @staticmethod
    def write_lock(path, packages):
        path.write_text(json.dumps({'dependencies': {'net10.0': packages}}))

    def write_consumer(self, version='2.1.0', dependency_hash='sealed dependency hash'):
        self.write_lock(self.consumer, {
            'example.dependency': self.package(version, dependency_hash),
            'Example.Library': self.package(self.policy['version'], 'registry-specific first-party hash'),
        })

    def test_accepts_sealed_dependency_and_checks_first_party_version(self):
        self.write_consumer()
        VERIFY.verify_consumer_lock(self.root, self.consumer, self.policy)
        self.policy['version'] = '1.0.0-rc.4'
        with self.assertRaisesRegex(ValueError, 'different release package version'):
            VERIFY.verify_consumer_lock(self.root, self.consumer, self.policy)

    def test_rejects_changed_hash_or_unsealed_version(self):
        for version, digest in (('2.1.0', 'different hash'), ('2.1.1', 'sealed dependency hash')):
            with self.subTest(version=version, digest=digest):
                self.write_consumer(version, digest)
                with self.assertRaisesRegex(ValueError, 'differs from sealed evidence'):
                    VERIFY.verify_consumer_lock(self.root, self.consumer, self.policy)

    def test_requires_release_locks(self):
        self.trusted.unlink()
        self.write_consumer()
        with self.assertRaisesRegex(ValueError, 'missing its runtime dependency locks'):
            VERIFY.verify_consumer_lock(self.root, self.consumer, self.policy)
