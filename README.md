# PerspexModpackPatcher

Perspex compatibility fixes and independently rebuilt features. Install the package on the client and server with Jötunn. It contains the gameplay plugin and a small BepInEx preloader that adapts Dekas' Valheim Legends 0.7.10 to Valheim 1.0.15 in memory before the plugin loads. The original Legends DLL stays unchanged. The ZIP includes no binaries from other mods.

## Repository layout

The root contains the gameplay patcher source, `PerspexModpackPatcher.csproj`, `manifest.json`, and packaging scripts (`build-package.ps1` and `package-modpack.py`). `preloader` contains the Legends compatibility patch. `authority` contains the restored PerspexCharacterAuthority source and project. `modpack-draft` contains the Perspex modpack manifest, README, changelog, and `BepInEx` configuration tree. `package-modpack.py` adds all three Perspex DLLs from the build output. Install the original mods listed in each manifest separately. Build outputs, extracted mod assemblies, game logs, and local profile backups are intentionally excluded from Git.

To build, install .NET SDK and set `ValheimPath` to your Valheim directory. The project also needs local copies of BepInEx, Jötunn, Dekas Valheim Legends 0.7.10, Professions and EpicMMOSystem at the reference paths in the project file; adjust those paths for your machine. Run `dotnet build PerspexModpackPatcher.csproj -c Release -p:ValheimPath="<Valheim directory>"`. The modpack draft has placeholder values for server address, password and webhooks; fill those only in a private deployment copy.

## Features

- Hearthstone, Marketstone and Deathstone consumables. Look at an owned bed and press **P** to set the Hearthstone destination. Home and death positions are stored with the player and can read prior Hearthstone position files.
- Consumable trophies for extra rows, quick slots, lightweight slots, ammo, misc, food and four utility slot levels. The trophies set player keys used by the modpack's ExtraSlots configuration.
- Coin cost for repairing worn equipment, based on the coin amount in its crafting recipe, quality, wear and a configurable multiplier. The repair tooltip lists costs, and coins are removed after a successful repair.
- Creature trophies grant configurable experience when EpicMMOSystem is installed. The original UsefulTrophiesXP DLL is not needed.
- Exploration map access only while the profession is selected, with a large-map exception while sitting near a fire, plus treasure bonuses. The conflicting map and treasure hooks from Professions and Exploration are removed when detected.
- Valheim Legends HUD placement and class storage in vanilla player data. PerspexCharacterAuthority saves the full player snapshot on the server, including this class value.
- Professions 1.4.7 patch for Craftsman Trophy slot purchases, 50% skill recovery on relearning, and levels in each profession's status square. `BlockExperience` permits use without XP; `BlockUsage` prevents use while inactive.
- Valheim Legends ability cost, cooldown and skill gain adjustments, plus point patches for the published attack methods, Priest and Druid healing, Shaman Spirit Drain, Monk damage and Surge, Power Shot, Execute, Duelist Riposte and selected class status effects.
- Metavoker Translocation: Block + Ability 2 opens a player picker for accepted Go To or Summon requests, with safe destinations, stamina cost, and cooldown.
- Shaman Chain Healing on Block + Ability 3, with healing reduced by 30% for each nearby ally healed.
- Druid Fenring shapeshift on Block + Ability 2, with Eitr sustain and Shadow Stalk, Stagger and Dash while transformed.
- Ranger wolf summons with synchronized scale, food-based healing and dismissal during Ability 2 cooldown.
- Monk Chi Power Up on Block + Chi Strike: spend 80% of maximum stamina for one Chi charge, up to three uses before a separate 120-second cooldown.
- Sit and use the third class ability to repair equipped gear: Duelist one-handed blades, Ranger bows and crossbows, Berserker one- or two-handed weapons, Valkyrie shields and Enchanter staves. Discipline or Alteration improves stamina efficiency; repair starts the ability cooldown.
- Munin class tutorials describe the modified abilities and show gemstone names instead of internal item identifiers.
- Metavoker Reactive Armor on Block + Ability 1, with charge release on Block + Ability 3.
- Mage Fire, Frost and Arcane affinities with charge regeneration, focus switching, meditation, independent Fireball/Meteor and Frost abilities, Arcane toggles, Eitr Shield, elemental damage passives and Thunderstone Surge.
- Enchanter elemental weapon and armor cycles, stack-based weapon procs, staff restoration while sitting and Thunderstone cooldown relief.
- Enchanter biome effects for Meadows, Black Forest, Swamp, Mountain, Plains, Ocean, Mist and Ash.
- Enchanter charm taming, expiry immunity and Charm Control; Shaman Windfury bonus strikes and cooldown reset; Druid root projectile immobilization.
- Local Berserker low-health damage curve, Priest club spirit damage, Ranger ranged criticals and Duelist one-handed melee criticals; the published Enchanter random elemental touch is disabled in favor of the reconstructed imbues.
- Duelist Challenge coin stakes and completion rewards, coin-cost Quick Shot, and Rogue Snatch rewards on eligible melee hits.
- CraftyCarts crafting station network view lookup through its parent cart when necessary.

## Installation

Install this package with a mod manager or put `PerspexModpackPatcher.dll` and `PerspexCharacterAuthority.dll` in `BepInEx/plugins` and `patchers/PerspexLegendsPreloader.dll` in `BepInEx/patchers` on every client and the server. The authority plugin must run on both sides; the server stores snapshots under `BepInEx/config/PerspexCharacterAuthority`. Its manifest declares Jötunn, Dekas Valheim Legends 0.7.10, Professions and EpicMMOSystem as dependencies. The Perspex modpack separately depends on BowsBeforeHoes 2.0.0, ItemRequiresSkillLevel and Exploration.

Remove the old `Hearthstone.dll`, `PlayerKeyTrophies.dll`, `RepairRequiresCoins.dll`, `UsefulTrophiesXP.dll`, `ExplorationMap.dll`, `ValheimLegendsHudFix.dll`, `BbhAmmoFix.dll`, `BbhTrace.dll`, `CraftyCartsValheim1Fix.dll` and standalone copies of `PerspexCharacterAuthority.dll` from the profile. The packaged authority DLL replaces the old one; do not load both copies together.

The patcher registers Hearthstone and Marketstone directly with embedded icons. The modpack supplies their recipes, Deathstone and slot trophies through WackysDatabase configuration. For a standalone installation without those definitions, enable `Items.RegisterFallbackItems` in `BepInEx/config/twentyonez.perspex.patcher.cfg` to register basic Deathstone and trophies cloned from Thunderstone. Hearthstone restrictions for portal items, enemies, encumbrance, water and resting are configurable there.

## Credits

The patcher operates on separately installed original mods. [Dekas' Valheim Legends Fork](https://thunderstore.io/c/valheim/p/Dekas/Valheim_Legends_Fork/) and [Smoothbrain's Professions](https://valheim.hexium.gg/mods/Smoothbrain/Professions) remain their authors' packages. The modpack installs [Azumatt's Bows Before Hoes](https://valheim.hexium.gg/mods/Azumatt/BowsBeforeHoes) directly; the patcher does not modify it.

## Current compatibility limits

Inactive professions follow each skill's `BlockExperience` or `BlockUsage` setting. Set `Diagnostics.HotbarInput=true` temporarily to log up to 64 physical presses of the 1–8 keys when diagnosing hotbar input on Windows.

The Legends integration targets Dekas 0.7.10. Static IL checks and compilation pass, but the authority handshake and full gameplay integration still need a client/server play test. **Full behavior parity with the customized local ValheimLegends source is not yet verified.** See `audit/PARITY.md`. The authority plugin stores `current.pca` and rotating backups under `BepInEx/config/PerspexCharacterAuthority/Characters/<account hash>/<character ID>/` on the server. The server and every connecting client need the same PerspexCharacterAuthority version.
