"""Check actual SDK package layouts in the read-only build before artifact upload."""
import json
import os
from pathlib import Path
import sys

from verify import sha256, verify_release

root = Path(sys.argv[1])
policy = json.loads(Path(__file__).with_name('policy.json').read_text())
verify_release(root, sha256(root / 'release.json'), os.environ['RELEASE_EXPECTED_COMMIT'], policy)
print('Verified the build candidate package layouts and source identities before upload.')
