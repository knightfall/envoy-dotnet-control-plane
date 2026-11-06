"""Archive the complete verified evidence, including dotfiles, without filtering it."""
from pathlib import Path
import sys
import zipfile


def create_archive(root, destination):
    root = Path(root).resolve(strict=True)
    with zipfile.ZipFile(destination, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(root.rglob('*')):
            if path.is_symlink():
                raise ValueError('Symbolic links are not release evidence')
            if path.is_file():
                archive.write(path, path.relative_to(root).as_posix())


if __name__ == '__main__':
    create_archive(sys.argv[1], sys.argv[2])
