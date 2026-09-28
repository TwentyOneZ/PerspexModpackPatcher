"""Import Perspex's numeric trophy XP balance, without copying mod code."""

import configparser
from pathlib import Path

root = Path(__file__).resolve().parent
source = root.parent / "PerspexModpack-1.3.0" / "BepInEx" / "config" / "gg.khairex.usefultrophies.twentyonez.EpicMMOPatch.cfg"
config = configparser.ConfigParser()
config.read(source, encoding="utf-8")
values = config["ExpScaling"]
(root / "TrophyXpDefaults.txt").write_text(
    "\n".join(f"{name}={float(amount):g}" for name, amount in values.items()) + "\n",
    encoding="utf-8",
)
print(len(values), "trophy XP values")
