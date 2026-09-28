"""Package Perspex configs, the merged gameplay plugin and its preloader."""
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

root = Path(__file__).resolve().parent
draft = root / 'modpack-draft'
manifest = json.loads((draft / 'manifest.json').read_text(encoding='utf-8'))
assert not any(dependency.startswith('TwentyOneZ-PerspexModpackPatcher-')
               for dependency in manifest['dependencies'])
files = sorted(path for path in draft.rglob('*') if path.is_file())
assert files and not any(path.name.lower().endswith(('.dll', '.dll.bak')) for path in files)
own_dlls = {
    'BepInEx/plugins/TwentyOneZ-PerspexModpack/PerspexModpackPatcher.dll': root / 'bin/Release/netstandard2.1/PerspexModpackPatcher.dll',
    'BepInEx/patchers/TwentyOneZ-PerspexModpack/PerspexLegendsPreloader.dll': root / 'preloader/bin/Release/netstandard2.1/PerspexLegendsPreloader.dll',
}
assert all(path.is_file() for path in own_dlls.values()), 'Build the patcher before packaging the modpack'
target = root / f"release/PerspexModpack-{manifest['version_number']}.zip"
with ZipFile(target, 'w', ZIP_DEFLATED) as archive:
    for path in files:
        archive.write(path, path.relative_to(draft).as_posix())
    for name, path in own_dlls.items():
        archive.write(path, name)
with ZipFile(target) as archive:
    assert archive.testzip() is None
    assert len(archive.namelist()) == len(files) + len(own_dlls)
    assert {name for name in archive.namelist() if name.lower().endswith('.dll')} == set(own_dlls)
    assert json.loads(archive.read('manifest.json'))['dependencies'] == manifest['dependencies']
print(f'{target}: {len(files)} config files; {len(own_dlls)} Perspex DLLs; no third-party DLLs')
