# Emberfall World Manifest

Authoritative data view of the baked world. QA is a double-check of THIS.

## Bounds
- Grid origin: (-260, -330)  size: 260x295 cells @ 2m
- World extent X: -260 .. 260   Z: -330 .. 260
- Mountain ring (playable edge) radius: 244m
- Player spawn: (0, 0.6, 194)

## Ground surfaces (cell count + bounding box)
- Mountain    26770 cells (~107080m2)  bbox x[-259..259] z[-329..259]
- Lava        10581 cells (~42324m2)  bbox x[-153..153] z[-315..-109]
- Grass       10871 cells (~43484m2)  bbox x[-197..197] z[-187..229]
- DirtField   17666 cells (~70664m2)  bbox x[-243..243] z[-141..223]
- Camp         1525 cells (~6100m2)  bbox x[-213..213] z[-139..73]
- WallBase      409 cells (~1636m2)  bbox x[-61..61] z[-61..61]
- CityStone    2515 cells (~10060m2)  bbox x[-57..57] z[-57..57]
- Water        5054 cells (~20216m2)  bbox x[-97..259] z[-7..259]
- DirtRoad      698 cells (~2792m2)  bbox x[-11..11] z[63..179]
- Glade         220 cells (~880m2)  bbox x[-21..21] z[179..209]
- Sand          391 cells (~1564m2)  bbox x[-83..77] z[187..237]

See ground_map.txt for the top-down ASCII layout.

## Objects
- **tree**: 411
- **rock**: 364
- **plant**: 885
- **flower**: 1013
- **building**: 13
- **prop**: 93
- **fence**: 62
- **deadtree**: 143

<details><summary>per type</summary>

- flower_gazania (flower): 1013
- fern_02 (plant): 821
- island_tree_01 (tree): 164
- LavaRockGround (rock): 145
- dead_tree_trunk (deadtree): 143
- boulder_01 (rock): 110
- namaqualand_boulder_02 (rock): 85
- island_tree_03 (tree): 83
- island_tree_02 (tree): 82
- tree_small_02 (tree): 80
- shrub_01 (plant): 64
- Fence (fence): 62
- RockCliff (rock): 24
- lamp_lantern (prop): 13
- MarketBarrel (prop): 13
- Chair (prop): 12
- WoodCrate (prop): 12
- Table (prop): 11
- lamp_globe (prop): 11
- Bed (prop): 8
- Bookshelf (prop): 5
- building_house (building): 5
- Cauldron (prop): 5
- MarketStall (building): 3
- building_shop (building): 3
- DungeonChest (prop): 2
- Campfire (tree): 2
- Anvil (prop): 1
- building_smithy (building): 1
- building_inn (building): 1

</details>

## Buildings (10)
- building_inn: pos (-40,46) size (18x14) entrance 270deg pad 2m
- building_house: pos (-18,44) size (16x14) entrance 0deg pad 2m
- building_shop: pos (39,42) size (22x18) entrance 0deg pad 2m
- building_shop: pos (18,42) size (16x14) entrance 0deg pad 2m
- building_smithy: pos (38,-36) size (20x18) entrance 90deg pad 2m
- building_shop: pos (18,-40) size (16x14) entrance 90deg pad 2m
- building_house: pos (46,-16) size (16x16) entrance 180deg pad 2m
- building_house: pos (-38,-38) size (22x18) entrance 90deg pad 2m
- building_house: pos (-18,-40) size (16x14) entrance 90deg pad 2m
- building_house: pos (-46,-16) size (16x16) entrance 270deg pad 2m

## Barriers
- Wall segments: 9
- Mountain ring beyond radius 244m (impassable edge)

## Conflicts: 0 — CLEAN, ship-ready

