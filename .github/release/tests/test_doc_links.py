import json
from pathlib import Path
import sys
import tempfile
import unittest
import warnings
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_doc_links


class PackageReadmeLinkTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        policy = self.root / '.github/release/policy.json'
        policy.parent.mkdir(parents=True)
        policy.write_text(json.dumps({'packages': ['Knightfall.Envoy.ControlPlane'], 'version': '1.2.3',
                                      'repository': 'owner/project'}), encoding='utf-8')
        self.readme = self.root / 'src/Envoy.ControlPlane/README.md'
        self.readme.parent.mkdir(parents=True)
        self.guide = self.root / 'docs/guide.md'
        self.guide.parent.mkdir()
        self.guide.write_text('# Guide, setup\n\n## Usage here\n\n## Usage here\n', encoding='utf-8')

    def check(self, content):
        self.readme.write_text(content, encoding='utf-8')
        return check_doc_links.check_package_readmes(self.root)

    def test_local_and_main_links_use_candidate_files_and_heading_ids(self):
        self.assertEqual(self.check('[local](../../docs/guide.md#usage-here)\n'
                                    '[main](https://github.com/owner/project/blob/main/docs/guide.md#usage-here-1)\n'
                                    '[external](https://example.test/missing)\n'), 1)

    def test_main_link_to_missing_candidate_file_fails(self):
        with self.assertRaisesRegex(ValueError, 'target is missing'):
            self.check('[guide](https://github.com/owner/project/blob/main/docs/missing.md)')

    def test_missing_local_file_and_missing_fragment_fail(self):
        for link, error in (('[missing](../../docs/missing.md)', 'target is missing'),
                            ('[missing](../../docs/guide.md#missing)', 'missing or unsupported fragment')):
            with self.subTest(link=link), self.assertRaisesRegex(ValueError, error):
                self.check(link)

    def test_relative_path_cannot_escape_checkout(self):
        with self.assertRaisesRegex(ValueError, 'outside the checkout'):
            self.check('[escape](../../../../outside.md)')

    def test_fenced_and_inline_code_are_not_links_or_headings(self):
        self.guide.write_text('# Guide\n```md\n## Hidden\n```\n## Visible\n', encoding='utf-8')
        self.check('`[missing](missing.md)`\n```md\n[missing](missing.md)\n```\n'
                   '[guide](../../docs/guide.md#visible)')
        with self.assertRaisesRegex(ValueError, 'missing or unsupported fragment'):
            self.check('[guide](../../docs/guide.md#hidden)')

    def test_reference_and_complex_headings_are_rejected(self):
        for link in ('[guide][reference]', '[shortcut]',
                     '<https://github.com/owner/project/blob/main/docs/guide.md>'):
            with self.subTest(link=link), self.assertRaisesRegex(ValueError, 'unsupported link syntax'):
                self.check(link)
        self.guide.write_text('# **Formatted**\n', encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'unsupported heading syntax'):
            self.check('[guide](../../docs/guide.md#formatted)')

    def test_packed_readme_must_match_checked_source(self):
        self.check('[guide](../../docs/guide.md#usage-here)')
        release = self.root / 'release'
        package = release / 'packages/Knightfall.Envoy.ControlPlane.1.2.3.nupkg'
        package.parent.mkdir(parents=True)
        with zipfile.ZipFile(package, 'w') as archive:
            archive.writestr('README.md', self.readme.read_bytes())
        self.assertEqual(check_doc_links.check_package_readmes(self.root, release), 1)
        with zipfile.ZipFile(package, 'w') as archive:
            archive.writestr('README.md', '[bad](../../docs/missing.md)')
        with self.assertRaisesRegex(ValueError, 'does not match the checked source'):
            check_doc_links.check_package_readmes(self.root, release)

    def test_missing_archive_and_missing_or_duplicate_packed_readme_fail(self):
        self.check('# Readme')
        release = self.root / 'release'
        package = release / 'packages/Knightfall.Envoy.ControlPlane.1.2.3.nupkg'
        package.parent.mkdir(parents=True)
        with self.assertRaises(FileNotFoundError):
            check_doc_links.check_package_readmes(self.root, release)
        with zipfile.ZipFile(package, 'w') as archive:
            archive.writestr('other.txt', 'other')
        with self.assertRaisesRegex(ValueError, 'expected exactly one README.md'):
            check_doc_links.check_package_readmes(self.root, release)
        with warnings.catch_warnings():
            warnings.simplefilter('ignore', UserWarning)
            with zipfile.ZipFile(package, 'w') as archive:
                archive.writestr('README.md', self.readme.read_bytes())
                archive.writestr('README.md', self.readme.read_bytes())
        with self.assertRaisesRegex(ValueError, 'expected exactly one README.md'):
            check_doc_links.check_package_readmes(self.root, release)


if __name__ == '__main__':
    unittest.main()
