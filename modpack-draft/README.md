# PerspexModpack

Perspex changes many parts of Valheim to emphasize different play styles, character choices, exploration and rewards. It aims for a slightly harder but rewarding experience. The modpack distributes configuration and depends on the original mods; its custom gameplay changes are supplied by PerspexModpackPatcher. Patch source: https://github.com/TwentyOneZ/PerspexModpackPatcher

Install with Gale, which resolves this pack's Hexium and Thunderstore dependencies in one profile. The pack uses Dekas' Valheim Legends Fork 0.7.10 as a separate dependency.

The bundled PerspexCharacterAuthority 1.0.6 must be installed on the server and every client. It saves each character on the server in `BepInEx/config/PerspexCharacterAuthority/Characters/<account hash>/<character ID>/current.pca`, with backups alongside it. The first join uses the bundled `TwentyOneZ.PerspexCharacterAuthority.cfg` settings and begins fresh progression.

PerspexModpackPatcher 0.2.19 also includes the MyriadJewels login performance fix. Remove any separate `MyriadJewelsPerformancePatch.dll` from older profiles to avoid duplicate patches.

Features:
<ul>
<li>Economy based on coins! Crafting and repairing items requires coins, as well as learning new professions.</li>
<li>Many classes with unique balanced abilities to choose from. You can change it easily anytime:</li>
<ul>
<li><b>Mage</b>: has offensive elemental magic, can regenerate Eitr instantly.</li>
<li><b>Druid</b>: balanced offensive magic and summons, based in conjuration. Also can heal and craft resurrection material (Ancient Seed).</li>
<li><b>Priest</b>: balanced offensive magic and summons, based in evocation. Also can heal and craft resurrection material cheaply (Bone Fragment).</li>
<li><b>Shaman</b>: offensive magic, extra melee attacks (windfury), area healing and buff. Also can heal and craft resurrection material (Thunderstone).</li>
<li><b>Ranger</b>: offensive ranged skill, tactical skills for positioning, can summon a wolf and craft arrows anywhere.</li>
<li><b>Berserker</b>: offensive melee skills for heavy damage, at the cost of own safety.</li>
<li><b>Valkyrie</b>: defensive melee skills for tanking damage and attacking back, can leap.</li>
<li><b>Metavoker</b>: can create illusions, light and control kinetic energy. Has a defensive reactive shield.</li>
<li><b>Duelist</b>: melee specialist, can shoot coins and challenge enemies for a duel, earning coins after defeating or overpowering them. Can repair an equipped one-handed bladed weapon while sitting.</li>
<li><b>Rogue</b>: have many tactical skills, poison specialist. Can pickpocket enemies to earn coins.</li>
<li><b>Monk</b>: balanced solo fighter, can heal self and deal heavy damage while unarmed or with fist weapons. Can concentrate their chi charges by attacking or powering up.</li>
<li><b>Enchanter</b>: have offensive and defensive elemental enchantments, can buff party according to the biome. Can charm monsters and weaken them.</li>
</ul>
<li>A level system with 6 attributes you are free to improve. These attributes affects class skills. Most equipment have requisites tied to these attributes, so pick your armor and weapon of choice!</li>
<li>A profession system with 12 skills: Alchemist, Blacksmith, Builder, Cook, Explorer, Farmer, Forager, Jeweler, Lumberjack, Miner, Rancher or Sailor. Most have unique features or items they can craft. Pick 1 (configurable), but you can change it anytime (lowers that skill level by half). Increase this number by using Craftsman Trophies you can buy at the market or drop from bosses.</li>
<li>You can mount boars, wolves and lox if a Rancher craft saddles for you!</li>
<li>You can build ships if you are a skilled Sailor!</li>
<li>You can build totems to flat the ground (mining), plant your crops (farming), build your houses (building) or even a ghost lumberyard to gather wood (lumberjack)!</li>
<li>Hearthstone system to take you home quickly!</li>
<li>Portals have more advanced, expensive and powerful versions that allows transportation of metals.</li>
<li>Craft jewels and socket them into your equipment for many unique effects.</li>
<li>Many backpack styles and tiers to help you carry your stuff.</li>
<li>Creatures and bosses get up to 5 stars, with increasing difficulty the farther you are from spawn.</li>
<li>Bosses drops unique armor sets and capes with unique bonuses!</li>
<li>Unlock the magic of Runestones!</li>
</ul>

Core mods include Jewelcrafting, Valheim Legends, Rune Magic and ValheimRAFT. The PerspexModpackPatcher adjusts separately installed mods and rebuilds former local features such as Hearthstone, trophy keys and coin repairs. The modpack contains no third-party mod DLLs.

<h2>Installation</h2>
<ol>
<li>Install Gale, which supports the modpack's Hexium and Thunderstore dependencies.</li>
<li>Click <strong>Install with Mod Manager</strong> button on top of the page. Allow the browser to open the Mod Manager.</li>
<li>Run the game via the mod manager, selecting it, choosing a profile and clicking on "start modded".</li>
</ol>

<h2>After-Installation Notes:</h2>
<ul>
<li>There are two saved NPCs that an admin can place anywhere (I suggest at the spawn). They will have quests and some stuff that will are meant to balance the coin earning and provide some basic tutorial for fishing.</li>
<li>Found out that if your Marketplace NPC doesn't work on a server, delete the kg.Marketplace.dll.mdb file from <b>the client's</b> BepInEx/plugins/KGvalheim-Marketplace_And_Server_NPCs_Revamped folder. If playing solo, set Use Marketplace Locally = true in MarketplaceAndServerNPCs.cfg config file.</li>
</ul>

<h2>Credits and original mods</h2>

The original mods remain the work of their authors. The PerspexModpackPatcher applies compatibility and gameplay patches to separately installed dependencies, or independently rebuilds features formerly supplied by local DLLs.

<ul>
<li><b>Valheim Legends:</b> original class mod by <a href="https://github.com/TorannD/ValheimLegends">TorannD</a>; continued by <a href="https://github.com/Visteus/ValheimLegends">Visteus</a> and <a href="https://github.com/giafosu/ValheimLegends">Gia</a>; the modpack depends on <a href="https://thunderstore.io/c/valheim/p/Dekas/Valheim_Legends_Fork/">Dekas' fork</a>. The patcher supplies the update for Deep North, new abilities, balance, HUD and class-state changes.</li>
<li><b>Professions and Exploration:</b> by Smoothbrain / blaxxun; original sources: <a href="https://github.com/blaxxun-boop/Professions">Professions</a> and <a href="https://github.com/blaxxun-boop/Exploration">Exploration</a>. The patcher changes profession selection and progression, and map and treasure rules.</li>
<li><b>EpicMMOSystem:</b> originally by <a href="https://github.com/Single-sh/EpicMMOSystem">Single-sh</a>, maintained in the <a href="https://github.com/Wacky-Mole/WackyEpicMMOSystem">WackyMole fork</a> used here. The patcher integrates trophy XP and addresses panel initialization.</li>
<li><b>CraftyCarts:</b> original mod and assets by <a href="https://github.com/rolopogo/ValheimMods">RoloPogo</a>, remake by <a href="https://github.com/AzumattDev/CraftyCartsRemake">Azumatt / OdinPlus</a>, Valheim 1.0 compatibility release by <a href="https://thunderstore.io/c/valheim/p/Gathering_Team/CraftyCartsRemake_1_0_Fix/">Gathering Team (wocky)</a>. The patcher adds a cart crafting-station fix.</li>
<li><b>Hearthstone:</b> the recreated stones are based on <a href="https://thunderstore.io/c/valheim/p/Detalhes/Hearthstone/">Detalhes' Hearthstone</a> and <a href="https://github.com/MenNoWar/HearthStone_revived">MenNoWar's HearthStone Revived</a>.</li>
<li><b>Useful Trophies:</b> original mod by <a href="https://github.com/Khairex/ValheimMods">Khairex</a>; independently implements the trophy XP integration.</li>
<li><b>Repair Requires Mats:</b> original mod by <a href="https://github.com/aedenthorn/ValheimMods">aedenthorn</a>. The patcher independently implements the coin-based repair mechanic.</li>
</ul>
