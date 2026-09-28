"""Check that the patcher package owns both stones and the modpack keeps their recipes."""
from pathlib import Path
from zipfile import ZipFile
import json

root = Path(__file__).resolve().parent
items = root / 'modpack-draft/BepInEx/config/wackysDatabase/Items'
recipes = root / 'modpack-draft/BepInEx/config/wackysDatabase/Recipes/Magic Crystal Table'
version = json.loads((root / 'manifest.json').read_text(encoding='utf-8'))['version_number']
with ZipFile(root / f'release/PerspexModpackPatcher-{version}.zip') as archive:
    assembly = archive.read('PerspexModpackPatcher.dll')
    for name in ('Hearthstone', 'Marketstone'):
        assert (root / f'Assets/{name}.png').read_bytes() in assembly, name
        assert not (items / f'Item_{name}.yml').exists(), name
        recipe = recipes / ('Recipe_RHearthstone.yml' if name == 'Hearthstone' else 'Recipe_Marketstone.yml')
        text = recipe.read_text(encoding='utf-8')
        assert f'clonePrefabName: {name}' in text and 'disabled: false' in text, name
print('Both stone icons embedded; no duplicate item definitions; modpack recipes retained.')
