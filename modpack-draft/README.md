# PerspexModpack 1.4.0 draft

This draft stages the Perspex gameplay configuration, the two TradersExtended JSON files, and dependencies. It contains no original mod DLLs. The patcher rebuilds the local DLL features or patches separately installed dependencies; ServerCharacters supplies server character storage. Server address, password and webhook values were replaced with placeholders. The dependency list and patched gameplay still need a client/server play test before publication.

Install with Gale, which can resolve Hexium and Thunderstore packages in one profile. The modpack uses Azumatt's BowsBeforeHoes 2.0.0 directly and lets it generate a fresh config on first launch. Valheim Legends comes from `Dekas-Valheim_Legends_Fork-0.7.10` on Thunderstore.

This modpack changes many aspects of the game, focus on diversity of gameplay styles, choices, exploration and rewards. This is aimed to be balanced to a slightly harder experience than vanilla with some hardcore, but rewarding mechanics.

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
<li>Portals have more powerful versions that allows transportation of metals.</li>
<li>Craft jewels and socket them into your equipment for many unique effects.</li>
<li>Many backpack styles and tiers to help you carry your stuff.</li>
<li>Creatures and bosses get up to 5 stars, with increasing difficulty the farther you are from spawn.</li>
<li>Bosses drops unique armor sets and capes with unique bonuses!</li>
<li>Unlock the magic of Runestones!</li>
</ul>

Core Mods: Jewelcrafting, Valheim Legends, Rune Magic, ValheimRAFT, and some quality of life mods. 
Gameplay adjustments to Valheim Legends and the rebuilt Hearthstone feature are supplied by PerspexModpackPatcher. The original Legends mod is installed as a separate dependency.

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
