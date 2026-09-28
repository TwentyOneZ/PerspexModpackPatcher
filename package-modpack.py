"""Package the dependency-only Perspex modpack from modpack-draft."""
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

root = Path(__file__).resolve().parent
draft = root / 'modpack-draft'
manifest = json.loads((draft / 'manifest.json').read_text(encoding='utf-8'))
assert any(dependency.startswith('TwentyOneZ-PerspexModpackPatcher-')
           for dependency in manifest['dependencies'])
files = sorted(path for path in draft.rglob('*') if path.is_file())
assert files and not any(path.name.lower().endswith(('.dll', '.dll.bak')) for path in files)
target = root / f"release/PerspexModpack-{manifest['version_number']}.zip"
with ZipFile(target, 'w', ZIP_DEFLATED) as archive:
    for path in files:
        archive.write(path, path.relative_to(draft).as_posix())
with ZipFile(target) as archive:
    assert archive.testzip() is None
    assert len(archive.namelist()) == len(files)
    assert json.loads(archive.read('manifest.json'))['dependencies'] == manifest['dependencies']
print(f'{target}: {len(files)} files; no mod DLLs')
