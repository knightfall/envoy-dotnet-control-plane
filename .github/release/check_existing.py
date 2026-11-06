"""Reject conflicting existing versions in either registry before any package push."""
import base64
import json
import os
from pathlib import Path
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request

from package_payload import payload

REGISTRIES = [('NuGet', 'https://api.nuget.org/v3/index.json'),
              ('GitHub', 'https://nuget.pkg.github.com/knightfall/index.json')]


class RegistryRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, target):
        if urllib.parse.urlsplit(target).scheme != 'https':
            raise ValueError('Registry redirects must use HTTPS')
        redirected = super().redirect_request(request, response, code, message, headers, target)
        if urllib.parse.urlsplit(target).netloc != urllib.parse.urlsplit(request.full_url).netloc:
            redirected.remove_header('Authorization')
        return redirected


def fetch(url, authorization, limit, allow_missing=False):
    headers = {'Authorization': authorization} if authorization else {}
    request = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.build_opener(RegistryRedirects()).open(request, timeout=30) as response:
            content = response.read(limit + 1)
            if len(content) > limit:
                raise ValueError('Registry response exceeds the expected size')
            return content
    except urllib.error.HTTPError as error:
        if allow_missing and error.code == 404:
            return None
        raise


def check_existing(root, policy):
    root = Path(root)
    credential = base64.b64encode(('knightfall:' + os.environ['GH_TOKEN']).encode()).decode()
    matched = {name: [] for name, _ in REGISTRIES}
    with tempfile.TemporaryDirectory() as directory:
        for name, index_url in REGISTRIES:
            authorization = 'Basic ' + credential if name == 'GitHub' else None
            index = json.loads(fetch(index_url, authorization, 1024 * 1024))
            base = next(resource['@id'] for resource in index['resources']
                        if resource['@type'] == 'PackageBaseAddress/3.0.0')
            address = urllib.parse.urlsplit(base)
            if address.scheme != 'https' or address.netloc != urllib.parse.urlsplit(index_url).netloc or address.query or address.fragment:
                raise ValueError('Unexpected registry package-content resource')
            for package_id in policy['packages']:
                package = package_id.lower()
                version = policy['version'].lower()
                filename = package + '.' + version + '.nupkg'
                original = root / 'packages' / (package_id + '.' + policy['version'] + '.nupkg')
                url = base.rstrip('/') + '/' + package + '/' + version + '/' + filename
                content = fetch(url, authorization, original.stat().st_size + 1024 * 1024, allow_missing=True)
                if content is None:
                    continue
                downloaded = Path(directory) / filename
                downloaded.write_bytes(content)
                if payload(downloaded) != payload(original):
                    raise ValueError('Existing ' + name + ' package conflicts with candidate: ' + package_id)
                matched[name].append(package_id)
    print('Existing package versions in both registries match the candidate; missing versions can be pushed.')
    return matched


if __name__ == '__main__':
    matched = check_existing(sys.argv[1], json.loads(Path(__file__).with_name('policy.json').read_text()))
    Path(sys.argv[2]).write_text(json.dumps(matched))
