# Emberfall World Manifest

Authoritative data view of the baked world. QA is a double-check of THIS.

## Bounds
- Grid origin: (-900, -1650)  size: 900x1325 cells @ 2m
- World extent X: -900 .. 900   Z: -1650 .. 1000
- Mountain ring (playable edge) radius: 900m
- Player spawn: (0, 0.9, 498)

## Ground surfaces (cell count + bounding box)
- Mountain    91464 cells (~365856m2)  bbox x[-899..899] z[-1649..999]
- DirtField  660855 cells (~2643420m2)  bbox x[-857..857] z[-1607..957]
- Lava       154817 cells (~619268m2)  bbox x[-263..263] z[-1607..-375]
- Camp         5862 cells (~23448m2)  bbox x[-591..613] z[-1491..421]
- DirtRoad     1016 cells (~4064m2)  bbox x[-17..27] z[-985..475]
- Grass      213676 cells (~854704m2)  bbox x[-709..709] z[-515..607]
- WallBase     1216 cells (~4864m2)  bbox x[-161..161] z[-161..161]
- CityStone   22141 cells (~88564m2)  bbox x[-157..157] z[-157..157]
- Water       36575 cells (~146300m2)  bbox x[-713..865] z[-9..985]
- Glade        1481 cells (~5924m2)  bbox x[-43..43] z[477..559]
- Sand         3397 cells (~13588m2)  bbox x[-153..155] z[547..857]

See ground_map.txt for the top-down ASCII layout.

## Objects
- **tree**: 4311
- **deadtree**: 398
- **plant**: 5952
- **rock**: 584
- **building**: 18
- **prop**: 362
- **fence**: 190

<details><summary>per type</summary>

- fern_02 (plant): 4781
- island_tree_01 (tree): 1720
- shrub_02 (plant): 1038
- island_tree_02 (tree): 863
- island_tree_03 (tree): 862
- tree_small_02 (tree): 858
- dead_tree_trunk (deadtree): 283
- namaqualand_boulder_02 (rock): 260
- Fence (fence): 190
- LavaRockGround (rock): 167
- boulder_01 (rock): 157
- shrub_03 (plant): 133
- dead_tree_trunk_02 (deadtree): 115
- ForestFoothill (prop): 100
- MountainRidge (prop): 69
- MountainPeak (prop): 35
- MarketBarrel (prop): 27
- WoodCrate (prop): 26
- Chair (prop): 22
- Bed (prop): 18
- Table (prop): 16
- Bookshelf (prop): 14
- lamp_globe (prop): 11
- lamp_lantern (prop): 10
- Cauldron (prop): 8
- Campfire (tree): 8
- building_house (building): 7
- building_shop (building): 3
- MarketStall (building): 3
- DungeonChest (prop): 3
- VolcanoErupting (prop): 1
- VolcanoDormant (prop): 1
- building_civic landmark (building): 1
- building_smithy (building): 1
- Anvil (prop): 1
- building_market anchor (building): 1
- building_water landmark (building): 1
- building_inn (building): 1

</details>

## Buildings (15)
- building_civic landmark: pos (0,48) size (56x40) entrance 90deg pad 2m
- building_inn: pos (-62,88) size (44x36) entrance 90deg pad 2m
- building_shop: pos (52,76) size (40x32) entrance 90deg pad 2m
- building_shop: pos (82,91) size (36x30) entrance 90deg pad 2m
- building_market anchor: pos (108,54) size (60x44) entrance 270deg pad 2m
- building_smithy: pos (66,-82) size (44x40) entrance 0deg pad 2m
- building_shop: pos (98,-65) size (40x34) entrance 270deg pad 2m
- building_water landmark: pos (-116,-52) size (48x40) entrance 180deg pad 2m
- building_house: pos (-140,112) size (32x30) entrance 180deg pad 2m
- building_house: pos (-122,72) size (34x30) entrance 180deg pad 2m
- building_house: pos (-75,122) size (32x28) entrance 90deg pad 2m
- building_house: pos (-72,-92) size (34x30) entrance 0deg pad 2m
- building_house: pos (-118,-94) size (36x32) entrance 180deg pad 2m
- building_house: pos (112,-108) size (34x30) entrance 270deg pad 2m
- building_house: pos (128,106) size (32x28) entrance 90deg pad 2m

## Barriers
- Wall segments: 8
- Mountain ring beyond radius 900m (impassable edge)

## Conflicts: 0 — CLEAN, ship-ready

