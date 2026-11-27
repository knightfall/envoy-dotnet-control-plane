"""Check resolved third-party dependencies against the release's sealed runtime locks."""
import json
from pathlib import Path
import sys


def packages(lock):
    for framework in json.loads(lock.read_text(encoding='utf-8-sig'))['dependencies'].values():
        for name, entry in framework.items():
            if entry['type'] != 'Project':
                yield name.lower(), entry['resolved'], entry['contentHash']


def verify_consumer_lock(release, consumer_lock, policy):
    release_ids = {name.lower() for name in policy['packages']}
    trusted = {}
    locks = sorted((release / 'evidence/src').glob('*/packages.lock.json'))
    if not locks:
        raise ValueError('Release evidence is missing its runtime dependency locks')
    for lock in locks:
        for name, version, content_hash in packages(lock):
            if not content_hash or trusted.setdefault((name, version), content_hash) != content_hash:
                raise ValueError(f'Conflicting or missing release dependency hash: {name} {version}')

    seen = set()
    for name, version, content_hash in packages(consumer_lock):
        if name in release_ids:
            if version != policy['version']:
                raise ValueError(f'Consumer resolved a different release package version: {name} {version}')
            seen.add(name)
        elif trusted.get((name, version)) != content_hash or not content_hash:
            raise ValueError(f'Consumer dependency is absent from or differs from sealed evidence: {name} {version}')
    if seen != release_ids:
        raise ValueError('Consumer lock is missing release packages')


if __name__ == '__main__':
    candidate, lock_file, policy_path = map(Path, sys.argv[1:])
    verify_consumer_lock(candidate, lock_file, json.loads(policy_path.read_text(encoding='utf-8-sig')))
    print('Resolved consumer dependency versions and hashes match sealed release evidence.')
