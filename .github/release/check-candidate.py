"""Check actual SDK package layouts in the read-only build before artifact upload."""
import json
import os
from pathlib import Path
import sys
import tempfile
import zipfile

from release_archive import create_archive
from verify import sha256, verify_release

root = Path(sys.argv[1])
policy = json.loads(Path(__file__).with_name('policy.json').read_text())
manifest_digest = sha256(root / 'release.json')
commit = os.environ['RELEASE_EXPECTED_COMMIT']
verify_release(root, manifest_digest, commit, policy)
with tempfile.TemporaryDirectory() as directory:
    bundle = Path(directory) / 'evidence.zip'
    create_archive(root, bundle)
    extracted = Path(directory) / 'extracted'
    with zipfile.ZipFile(bundle) as archive:
        archive.extractall(extracted)
    verify_release(extracted, manifest_digest, commit, policy)
print('Verified SDK package layouts, source identities and the complete evidence ZIP round trip.')
