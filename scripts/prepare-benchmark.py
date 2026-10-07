"""Install only the checksum-pinned Linux RNNoise library used by the benchmark."""
import hashlib
import io
from pathlib import Path
import sys
import urllib.request
import zipfile

url = "https://files.pythonhosted.org/packages/f1/8b/6498de7ef0a4670bbeae951bec9cfd2386e57ef386f53eec28e75be39790/pyrnnoise-0.4.5-py3-none-manylinux1_x86_64.whl"
expected = "c095114532f9fa21f621084c879d72c93aaa0c9dbe38b662a63577bada2c214e"
data = urllib.request.urlopen(url, timeout=60).read()
if hashlib.sha256(data).hexdigest() != expected: raise ValueError("RNNoise wheel checksum mismatch")
target = Path(sys.argv[1]); target.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(io.BytesIO(data)) as wheel:
    library = wheel.read("pyrnnoise-0.4.5.data/purelib/pyrnnoise/librnnoise.so")
    (target / "librnnoise.so").write_bytes(library)
    (target / "LICENSE").write_bytes(wheel.read("pyrnnoise-0.4.5.dist-info/licenses/LICENSE"))
print((target / "librnnoise.so").resolve())
