"""Package only this mod's binaries and metadata, never reference assemblies."""
import json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parent.parent
version = json.loads((root / "manifest.json").read_text(encoding="utf-8"))["Version"]
files = [
    (root / "bin/Release/net6.0/ProductionProgress.dll", "ProductionProgress.dll"),
    (root / "bin/Release/net6.0/ProductionProgress.Core.dll", "ProductionProgress.Core.dll"),
    (root / "manifest.json", "manifest.json"),
    *((path, "i18n/" + path.name) for path in sorted((root / "i18n").glob("*.json"))),
]
for source, _ in files:
    if not source.is_file():
        raise SystemExit(f"Missing build output: {source}")
output = root / "artifacts" / f"ProductionProgress-{version}-preview.zip"
output.parent.mkdir(exist_ok=True)
with ZipFile(output, "w", ZIP_DEFLATED) as archive:
    for source, relative in files:
        archive.write(source, "ProductionProgress/" + relative)
with ZipFile(output) as archive:
    if archive.testzip() is not None:
        raise SystemExit("Package integrity check failed")
print(output)
