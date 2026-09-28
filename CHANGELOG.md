# Changelog

## 0.2.16 — 2026-09-28

- Added an early in-memory compatibility pass for the published Dekas Legends 0.7.10 DLL on Valheim 1.0.15. It updates obsolete status, message and effect calls without redistributing or overwriting the original DLL.
- Updated Legends summon, charm and Spirit Drain hooks to the current status-effect signature, and load the Legends dependency before the gameplay patcher.
- Package now includes `patchers/PerspexLegendsPreloader.dll` alongside the gameplay plugin.

## 0.2.15 — 2026-09-27

- Refined Munin class and offering descriptions with readable gemstone names, including Diamond and Amethyst.

## 0.2.14 — 2026-09-27

- Register Hearthstone and Marketstone as patcher-owned consumable prefabs on every installation, with embedded icons. Their recipes remain in the modpack configuration.
- Keep optional fallback registration for Deathstone and slot trophies only.

## 0.2.13 — 2026-09-27

- Fixed the Chi Power Up ObjectDB Harmony postfix signature, which prevented ObjectDB from initializing and left the game on the loading screen.

## 0.2.12 — 2026-09-27

- Restored Monk Chi Power Up on Block + Chi Strike with an independent three-use, 120-second cooldown, 80% maximum stamina cost, one Chi charge and its original effects.
- Moved Duelist weapon repair to sitting + S. Slash and added seated repair for Ranger bows and crossbows, Berserker weapons, Valkyrie shields and Enchanter staves. Repair uses Discipline or Alteration, consumes stamina proportional to restored durability and starts the ability slot cooldown.
- Updated all class tutorials from the modified Legends descriptions, documented the new controls and replaced ten uncut-stone identifiers with gemstone names in Munin help.

## 0.2.11 — 2026-09-27

- Prevent Zone Shock from triggering after Zone Charge completes unless Block is held with a new Zone Charge press.

## 0.2.10 — 2026-09-27

- Detect Enchanter biome buffs by their stable status identifiers so Block + Zone Charge triggers Zone Shock even while Zone Charge is cooling down.

## 0.2.9 — 2026-09-27

- Restored class item crafting for Ranger arrows and Druid, Priest and Shaman resurrection vials, including materials spread across inventory stacks.
- Allowed manual Druid shapeshift cancellation during cooldown; on class changes, dismiss owned summons and reduce remaining class cooldown time to 10%.
- Matched dual weapon stamina use for Berserker and dual knives for Rogue to one weapon, restored the Reactive Armor shield staff icon and corrected Shadow Wolf damage scaling.
- Allowed Enchanter enchantment cycling during cooldown and Zone Shock consumption of an active biome buff, with damage based on the buff's remaining time. Kept Thunderstone cooldown reset available without a buff.
- Fixed the repeated Enchanter `InvalidCastException` caused by reading an integer charge counter as a float.

## 0.2.8 — 2026-09-27

- Restored Mage focus, recharge and meditation visuals and sounds; Frost Nova and Blizzard visuals; Fireball cast effects; Arcane toggle, fade and shield effects; and the original item icons for affinity, Arcane buffs and Frozen.
- Restored effects omitted from rebuilt Enchanter imbuements and procs, Reactive Armor, Fenring transformation, Duelist coin skills and Charm Immunity.

## 0.2.7 — 2026-09-27

- Removed all BowsBeforeHoes hooks, including quiver UI suppression and ammo integration, now that the modpack uses the author's Valheim 1.0 build (2.0.0).
- Removed BowsBeforeHoes from the patcher's own dependencies. The modpack depends directly on BowsBeforeHoes 2.0.0 and no longer includes BowsBeforeHoesCompat.

## 0.2.6 — 2026-09-27

- Trigger hotbar diagnostics from physical Windows key state, independently of Unity input, and record Unity focus and key states alongside ZInput and hotbar use.

## 0.2.5 — 2026-09-27

- Extended the opt-in hotbar diagnostic to record up to 64 key presses, so input can be compared before and after it stops working in the same session.
- Restored Rogue Smoke Bomb when Ability 1 is used while crouching, including its reduced stamina cost, effects, enemy target reset, cooldown and Illusion skill gain.

## 0.2.4 — 2026-09-27

- Restricted the large map to players with Exploration selected, except while sitting near a fire; the small minimap also requires selected Exploration and its configured level. Sailing and cartography tables no longer bypass this rule.
- Added an opt-in, one-shot hotbar input diagnostic. Static inspection found no patcher hook that consumes vanilla hotbar keys; the active profile enables this diagnostic to identify whether the physical key reaches ZInput and `Player.UseHotbarItem`.

## 0.2.3 — 2026-09-27

- Respected Professions' `BlockExperience` and `BlockUsage` settings instead of forcing every skill to `BlockUsage`; inactive skills in `BlockExperience` remain usable without XP.
- Moved profession levels out of skill names and into the status square above each selection button, including Jewelcrafting, Foraging and Exploration.
- Allowed Exploration map actions under `BlockExperience` while still preventing XP for an inactive profession.

## 0.2.2 — 2026-09-27

- Disabled the two incompatible BowsBeforeHoes quiver UI hooks that prevented the inventory and EpicMMO menu from opening. Quiver item storage and ammo integration remain available; visual hotkeys require a separate upstream compatibility fix.
- Enforced active profession selection for skill XP and usage, regardless of the Professions configuration, and prevented inactive Exploration from gaining chest XP.

## 0.2.1 — 2026-09-27

- Prevented the spawn loop caused by Dekas Legends `SetVLPlayer` reading an uninitialized class list; class selection now initializes from player data before the spawn hook returns.
- Initialized EpicMMO's level XP table on first use to stop repeated panel errors before its normal setup completes.
- Installed quiver exception guards against the actual BowsBeforeHoes assembly, including its player update hook, so incompatible quiver code cannot flood the log each frame.

## 0.2.0 — 2026-09-27

- Rebuilt Druid Roots channel over Dekas 0.7.10 with local projectile cadence, stamina drain, damage and movement behavior.
- Disabled fallback item registration by default because Perspex supplies the stone and trophy prefabs through WackysDatabase.
- Staged the active profile configuration without mod DLLs and scrubbed webhook credentials for the modpack draft.
- Limited the patcher's dependency manifest to the five mods it loads directly; optional integrations remain in the modpack manifest.
- Prepared the Perspex test profile with Dekas 0.7.10 and the patcher, disabling ten replaced local DLLs with restorable backups.
- Migrated Enchanter biome status formulas and their damage, regeneration, carry, movement, block and cooldown hooks over the published Legends DLL.
- Corrected Metavoker Warp's published per-update stamina drain to the local per-second cost.
- Restored the local companion scale synchronization and distance catch-up behavior over the published summon status.
- Matched companion expiry and damage effects and suppressed the published target selection tick removed by the local source.
- Matched local Chain Healing radius scaling and the Legends cooldown getter.
- Migrated Rogue and Valkyrie charge timing, Ranger and Rogue movement, Bulwark mitigation, Shadow Stalk speed, and Monk surge timing over the published status effects.
- Preserved local class gates and fixed Execute defaults for the published status effects.
- Migrated Ranger wolf health/scale, healing and dismissal during cooldown, and Shadow Stalk duration scaling.

- Rebalanced published Legends damage entry points for Berserker, Druid roots, Duelist, Enchanter, Mage, Metavoker, Monk, Priest, Rogue, Shaman and Valkyrie without bundling the original DLL.
- Added point patches for Priest and Druid healing, caster snapshot damage for four summon types, Spirit Drain RPC attribution, Execute threshold, Ranger Power Shot, Monk Surge, and selected class status effects.
- Rebuilt Duelist's Block + Ability 3 weapon sharpening and guarded its stamina calculation against zero-cost overflow.
- Rebuilt Druid Fenring form as an independent status over Dekas 0.7.10, including shapeshift input, sustain, visuals, double jump, borrowed abilities, unarmed damage and block bonuses.
- Replaced published Monk's flat unarmed damage multiplier with the local blunt/spirit formula, Chi cap, skill gain and adjusted unarmed block bonus.
- Replaced published Rogue's flat dagger multiplier with the local dagger poison bonus and skill progression.
- Moved Weaken's attacker stamina refund to the incoming damage hook, matching the local fork's timing without double refunds.
- Rebuilt Metavoker Reactive Armor with charge absorption, stamina scaling, manual release and cooldown adjustment.
- Rebuilt Mage affinity charges, focus switching and meditation, Fire and Frost abilities, Arcane buffs, Eitr Shield, focus damage passives and Thunderstone Surge over Dekas 0.7.10.
- Added Enchanter elemental weapon and armor cycles, stack procs, staff restoration and Thunderstone relief for Zone Charge.
- Added Charm Control, Shaman Windfury and Druid Rooted effects over the original Legends DLL.
- Replaced the published Enchanter random elemental touch with reconstructed imbues; added local Berserker, Priest, Ranger and Duelist damage passives.
- Added Duelist Challenge and coin-cost Quick Shot, plus Rogue Snatch rewards.
- Patched Duelist Riposte to use weapon damage, scaled parry skill and an eight-metre reach; restored its Rogue/Duelist class gate.
- Verified additional 0.7.10 IL call sites and isolated patch installation errors so one failed group does not prevent unrelated integrations from loading.
- Professions trophy purchases now consume the required amount across multiple inventory stacks; panel text no longer duplicates after refresh.
- Added a source-only Professions 1.4.7 patch for Craftsman Trophy slots, half-level relearning, and panel labels.
- Added source-only Valheim Legends cost and cooldown getter patches, followed by independent class abilities and status effects.
- Added Metavoker Translocation with remote consent and Shaman Chain Healing as independent patches over Dekas 0.7.10.
- Save the Legends class in player data and disable its separate character-file hooks; start class persistence from zero.
- Require Dekas 0.7.10 before installing Legends hooks and list the original mod dependencies in the manifest.
- Corrected the dependency audit: the profile's 0.5.3 DLL and backup differ from published Dekas 0.7.10.
- Verified patch targets and local reference getter values; an in-game client/server test remains necessary.

## 0.1.0 — 2026-09-26

- Created a clean single DLL package without redistributing other mods.
- Rebuilt teleport stones, player key trophies, repair coin costs, trophy XP and exploration rules.
- Added optional Legends HUD and class persistence, BowsBeforeHoes quiver fixes compatible with GenesisMods' preloader, and the CraftyCarts network view fix.
- Added standalone Hexium manifest, README and 256×256 icon packaging.
