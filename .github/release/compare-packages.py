"""Compare package payloads; NuGet adds its repository signature after upload."""
import hashlib
import sys
import zipfile


def payload(path):
    with zipfile.ZipFile(path) as archive:
        entries = [item for item in archive.infolist() if item.filename != '.signature.p7s']
        names = [item.filename for item in entries]
        if len(names) != len(set(names)):
            raise ValueError('Duplicate package entries')
        return {item.filename: hashlib.sha256(archive.read(item)).hexdigest() for item in entries}


if __name__ == '__main__':
    if payload(sys.argv[1]) != payload(sys.argv[2]):
        raise ValueError('Published package payload differs from the verified release artifact')
    print('Published package payload matches the verified artifact.')
