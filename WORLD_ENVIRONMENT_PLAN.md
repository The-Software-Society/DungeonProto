# Emberfall — Environment & Waterworks Milestone

Goal: a natural, intentional ecosystem. Every water body sourced (waterfall -> river ->
lake), biome-appropriate ground/foliage, real mountains, dirt roads, room to breathe.
Terrain is the base (revived + working); this milestone is the world DESIGN on top of it.

## 1. World expansion
- Grow terrain from 520x592 to ~900x1040 (x[-450,450], z[-470,570]).
- City stays at origin; volcano/ember stay south. The new room goes NORTH + flanks for the
  lake, its buffer, and real mountains.

## 2. Mountains = terrain, not cube walls
- Heightmap rises from valley floor (city/glade/lake ~y0) -> foothills -> a real mountain
  ring at the outer edge (peaks +35-55m), rock + snow splat. Kill/retire the black cube
  "Massif" wall (distant silhouette only). This removes the black blocks in every view.

## 3. The Lake (city-sized, cradled with buffer)
- LakeCenter (0, 300), radius ~95 (~190m across, bigger than the city). A north-valley bowl.
- Progression northward: CITY (0,0) -> GLADE/spawn (0,90) -> DARK WOODS (0,195) -> LAKE (0,300).
- 70-90m of forested buffer (trees + mobs + rocks) between the lake shore and the mountains.
  The lake NEVER touches the mountain wall.

## 4. Waterworks (intentional: source -> river -> lake; NO random water)
Four waterfalls on the mountains, each feeding a river that drains to the lake:
- N Falls  (0, 520)    -> gorge river S -> lake NORTH shore
- NE Falls (300, 470)  -> river SW      -> lake NE shore
- NW Falls (-300, 470) -> river SE      -> lake NW shore
- E Falls  (430, 40)   -> river W THROUGH THE CITY (a canal) -> N to the lake SE inlet
Lake OUTFLOW: a stream drains S down the ember approach and is consumed at the lava
(water meets fire). Rivers: deep/swimmable, gradient banks. Every body is on this chain.

## 5. Biomes (ground + foliage per zone)
- Waterside (lake/river banks): wet lush grass, reeds/cattails, lily pads, mud/pebble shore.
- Forest (ring + buffer): dense grass, ferns, bushes/shrubs, fallen logs, mushrooms, trees.
- Meadow (glade + open valley): grass + wildflowers.
- Volcanic (south ember + volcano): cracked dry dirt, ash, charred stumps, sparse dead
  brush, lava glow + ember particles.
- City: cobble streets + dirt roads radiating out + grass verges.

## 6. Roads
Dirt-road network (worn splat + slight recession): city gates -> glade -> lakeside;
city -> each camp; a road following the city river.

## Build order (each verified by editor screenshot + host-boot)
1. Expand terrain + terrain mountain ring (retire cube wall) + valley.
2. Move/enlarge the lake to the north bowl with buffer.
3. River channels: 4 waterfall sources -> rivers -> lake, + the city river; lake outflow.
4. Biome splat layers (waterside/forest/meadow/volcanic/city) + rock/snow on mountains.
5. Biome foliage prototypes (reeds, ferns, bushes, dead brush) as terrain details.
6. Dirt-road network.
7. Full boot-verify + deploy for a live walkthrough.

## Macro to confirm with Jon
- Lake north-central at (0,~300), ~190m across. (vs. keep NW / go central?)
- World ~1.75x bigger, mostly north.
- River-through-city = east-sourced canal cutting W through the city.
