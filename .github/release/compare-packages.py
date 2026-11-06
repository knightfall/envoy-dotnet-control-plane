"""Compare package payloads; NuGet adds its repository signature after upload."""
import sys
from package_payload import payload


if __name__ == '__main__':
    if payload(sys.argv[1]) != payload(sys.argv[2]):
        raise ValueError('Published package payload differs from the verified release artifact')
    print('Published package payload matches the verified artifact.')
