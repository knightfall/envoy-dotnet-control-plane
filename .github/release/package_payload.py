"""Read package content independently of NuGet's added repository signature."""
import hashlib
import zipfile


def payload(path):
    with zipfile.ZipFile(path) as archive:
        entries = [item for item in archive.infolist() if item.filename != '.signature.p7s']
        names = [item.filename for item in entries]
        if len(names) != len(set(names)):
            raise ValueError('Duplicate package entries')
        return {item.filename: hashlib.sha256(archive.read(item)).hexdigest() for item in entries}
