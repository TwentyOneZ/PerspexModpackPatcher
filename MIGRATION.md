# Perspex 1.4.0 publication status

The 0.2.14 patcher ZIP and a 1.4.0 modpack draft ZIP are in `release`. The `modpack-draft` folder contains the 1.4.0 modpack metadata with `PerspexModpackPatcher` and `Smoothbrain-ServerCharacters-1.4.17` dependencies. Character persistence may start from zero, so no PerspexCharacterAuthority snapshot importer is needed.

The supplied `PerspexModpack-1.4.0/BepInEx/config` is empty. `modpack-draft` stages the profile configuration and the two TradersExtended JSON files used by This Goes Here. The former BowsBeforeHoes 1.3.14 config was removed, following the 2.0.0 author's upgrade note, so the mod generates a fresh config. The staging script strips active webhook credentials and passwords and omits original mod DLLs. Review distribution rights for included images before publishing the **modpack**.

| Old local DLL | Replacement |
| --- | --- |
| `Hearthstone.dll` | Rebuilt stone items and saved destinations in the patcher |
| `PlayerKeyTrophies.dll` | Rebuilt consumable player key trophies in the patcher |
| `RepairRequiresCoins.dll` | Independent coin repair logic in the patcher |
| `UsefulTrophiesXP.dll` | Independent trophy XP logic in the patcher; EpicMMOSystem remains a separate dependency |
| `ExplorationMap.dll` | Rebuilt map and treasure rules in the patcher; Professions and Exploration remain separate dependencies |
| `ValheimLegendsHudFix.dll` | Optional patch over the original Legends DLL |
| `BbhAmmoFix.dll`, `BbhTrace.dll` | Omit; the modpack now uses BowsBeforeHoes 2.0.0 directly, without patcher hooks |
| `CraftyCartsValheim1Fix.dll` | Optional patch over CraftyCarts |
| `PerspexCharacterAuthority.dll` | ServerCharacters dependency for server character storage; patcher stores the Legends class in player data |
| `ValheimLegends.dll.bak` | Omit; this backup is byte-identical to the formerly installed modified 0.5.3 DLL. The test profile now has the published Dekas 0.7.10 DLL; the separate local source has additional changes whose parity remains unverified. |
| `Farming.dll.bak` | Omit; the local Farming checkout changes only its build copy destination. The restored DLL is byte-identical to the Hexium 2.2.3 package. |
| `Professions.dll.bak` | A source patch now covers Craftsman Trophy profession slot progression, partial skill retention, and panel indicators over Hexium 1.4.7. It compiles, but has not yet been validated in a client/server play test. |

The staged `BepInEx/config/Valheim.ThisGoesHere.Perspex.yml` contains only the two TradersExtended JSON rules. `audit/install_test_profile.py` installed the published Legends 0.7.10 in the active profile, disabled ten replaced local DLLs, and backed them up under `obj/profile-before-patcher-0.2.0`. The active profile now has patcher 0.2.14; restart the game to load it. The Legends spawn, hotkeys and Rogue Smoke Bomb passed client tests on earlier builds. Mage effects restored in 0.2.8 still need an in-game retest. See `audit/PARITY.md` for the static comparison and `audit/HOTBAR-2026-09-27.md` for the input diagnosis.

The modpack now declares `Azumatt-BowsBeforeHoes-2.0.0` without the GenesisMods compatibility preloader. Patcher 0.2.14 contains no BowsBeforeHoes hooks or assembly reference. The old profile config is backed up at `obj/Azumatt.BowsBeforeHoes-1.3.14-profile.cfg`; review any desired custom values after 2.0.0 creates its new config. `Dekas-Valheim_Legends_Fork-0.7.10` is published on Thunderstore, and Gale supports mixed Hexium and Thunderstore profiles. This modpack should be installed through Gale while that package remains Thunderstore-only. Runtime tests with the target game and all optional dependencies remain necessary; `dotnet build` proves compilation only.
