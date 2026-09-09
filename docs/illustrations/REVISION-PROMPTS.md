# Revised FreshWorld illustration prompts

Mode: built-in image generation and editing. The user requested replacement of the earlier illustrations. Existing PNGs are the edit targets; the user's circular-arrow image is the reset-symbol reference. English labels only. No manual raster edits.


## Final correction prompts

### Last local correction: SafeZones = 2

```text
Make ONLY this tiny local correction to the supplied SafeZones = 2 infographic: In the RIGHTMOST "LOCATION RESET" map, row 1 column 1 (its upper-left grid cell), erase the small green circular-arrow symbol next to the pine tree. Keep the tree and all other pixels/layout/text unchanged. That cell contains no dungeon and must NOT show a location-reset symbol. Do NOT remove any green arrow beside a stone dungeon entrance elsewhere. Do NOT change any blue protected cell, house, terrain circle, grid boundary, or other panel. Preserve the image exactly apart from this one mistaken green symbol.
```

These edits follow the initial redraw prompts below and constrain reset symbols to the intended targets.

### SafeZones = 0

```text
Edit this SafeZones = 0 infographic with only these precise corrections. Preserve all English text, colors, exact 5x5 grid geometry, ZERO blue protected cells, single brown house icon convention and small two-curved-arrow green RESET symbols.
1. LOCATION RESET map ONLY: REMOVE every green circular reset symbol next to a TREE or ORE. Natural trees/ore should remain solid and unchanged, with NO reset symbol. Place the SAME green circular reset symbol ONLY beside each STONE DUNGEON ENTRANCE, including the central dungeon next to the deleted house. This is crucial: location reset is not a vegetation reset.
2. VEGETATION RESET map: replace the three eligible stumps (row1 col2, row2 col5, row4 col4) with healthy regenerated trees, preserving the circular reset symbols. No depleted stumps in any eligible cell on this SafeZones 0 page. Keep the central solid house intact, and add a small green regenerated tree beside that house with a reset symbol so vegetation in its zone is clearly affected too. Preserve the copper-centered terrain radius annotation.
3. ZONE RESET map: add a small healthy regenerated tree with a green circular reset symbol beside the red dashed deleted house in the central cell. The natural world regenerates there too; the player house remains deleted. Keep all other natural regenerated icons and green arrows unchanged.
Do not add solid houses outside the central cell. Do not put circular reset symbols on houses. Keep the location-radius inset, with a deleted house inside and intact house outside, unchanged. English only.
```

### SafeZones = 1

```text
Edit this SafeZones = 1 infographic with only these precise corrections. Preserve all text, exact 5x5 grids, exactly ONE central blue protected cell in EVERY map, single brown house symbol convention, and green circular two-curved-arrow RESET symbol.
1. ZONE RESET map: ensure EVERY GOLD eligible cell has one small green circular reset symbol beside its regenerated tree/ore/dungeon icon, including all eligible cells that currently lack the symbol. Do not add a symbol inside the central BLUE cell. Do not erase natural objects.
2. VEGETATION RESET map: replace ALL stumps in GOLD eligible cells with healthy regenerated green trees and small green circular reset symbols. All GOLD tree/ore cells should have a reset symbol. Only the BLUE central cell is skipped and gets no reset symbol. Preserve its intact house. Preserve the copper-centered terrain radius r.
3. LOCATION RESET map: preserve circular reset symbols ONLY beside STONE DUNGEON ENTRANCES in GOLD eligible cells. No reset symbols beside trees or ore or in the blue cell.
4. Bottom-right inset: make the SINGLE BLUE ZONE TILE wide enough to contain BOTH intact houses and the dungeon entrance. The left house lies inside the orange entrance-centered circle, the right house lies outside that circle BUT STILL ON THE SAME BLUE TILE. Currently the right house is incorrectly outside the blue zone; fix this. Keep both houses intact and the label "Base zone skipped: both buildings retained". No red X or reset arrows in this inset. Center the circle on the dungeon entrance.
Keep everything else, including footer and hierarchy, unchanged.
```

### SafeZones = 2

```text
Edit this SafeZones = 2 infographic with only these precise corrections. Preserve all English text, exact 5x5 grids, exactly the central 3x3 block of NINE blue protected cells and 16 GOLD outer-ring cells in EVERY map. Preserve the single brown house icon convention.
1. LOCATION RESET main map ONLY: REMOVE every green circular reset symbol beside a TREE or ORE. Keep those trees and ores solid unchanged. Place the same small green TWO CURVED ARROWS reset symbol ONLY beside the stone DUNGEON ENTRANCES in the eligible GOLD outer ring. No reset symbol in any BLUE cell. This is crucial: location reset is not vegetation reset.
2. ZONE RESET map: ensure EVERY eligible GOLD outer-ring cell has the green circular reset symbol beside its regenerated natural tree/ore/dungeon, including GOLD dungeon cells that currently lack arrows. Keep all natural scenery, NO arrows in the blue3x3.
3. VEGETATION RESET map: preserve its valid diagram. All eligible outer-ring trees and copper are regenerated with reset arrows; central blue area is unchanged. Do not disturb the copper-centered radial terrain circle.
4. Add a SMALL unchanged stone dungeon entrance BESIDE the solid house inside the central BLUE cell of the LOCATION main map, so its skipped state corresponds to the inset. Both fit within the central cell. Preserve the blue inset with both houses intact. No red deletion marks on maps.
Keep everything else unchanged. No arrows on houses or skipped cells.
```

## Initial redraw prompts

## SafeZones = 0

```text
Use case: infographic-diagram, educational game-mod illustration edit.
Image 1 is the old FreshWorld infographic: redraw its maps to fix the misleading permanent-clearing impression and simplify the icons, while retaining the clean three-column layout, warm off-white background, English labels, and precisely countable 5x5 square grids. Image 2 is the user's reset-symbol reference: two bold curved arrows chasing each other in a circle. Use that silhouette, recolored dark green, consistently to mean RESET / REGENERATE. This is not a delete symbol.

Landscape 1536x1024 or larger in the same 3:2 ratio. Crisp, restrained game-manual illustration with simple wood house, green tree, copper ore and stone dungeon entrance icons. Reduce decorative clutter. Exactly 5 rows and 5 columns per map, with row/column numbers 1–5; every grid cell boundary must remain clear.
Three headings: "ZONE RESET" / "zones_reset", "VEGETATION RESET" / "vegetation_reset", "LOCATION RESET" / "locations_reset".
Top legend: green circular arrows "Reset / regenerate", blue square "Skipped zones", wood HOUSE "Base with protection marker", red dashed HOUSE + small X "Player building removed". NO PORTALS, gates, separate marker objects, standalone marker poles or blue building types. Every solid house uses ONE identical simple brown house icon. The house is a schematic base containing a configured marker; it does not claim arbitrary pieces qualify in the game.

CRITICAL: ALL HOUSE SYMBOLS ON EACH MAIN MAP must be confined to row 3 column 3, the central cell. Remove ALL former houses from other cells, replacing them with tree/ore/dungeon icons. This ensures the depicted 1-cell / 3x3 protection areas are consistent; there are no unprotected bases elsewhere. The three columns independently show the same base position.

ZONE RESET: depict the FINAL REGENERATED NATURAL WORLD after normal world loading, NOT the temporary empty state. All eligible cells contain healthy regenerated green trees, ores and a few stone dungeon entrances, with green circular reset-arrow symbols distributed across the eligible area. NEVER use red dashed trees, tree X marks, empty cleared land, destroyed-tree icons or a forest disappearing. The green arrows mean natural vegetation / terrain / locations are reset. Player-built houses are NEVER regenerated. Protected blue cells show their unchanged world, with no reset arrows. Caption exactly: "Natural world regenerates on loading." and "Player buildings in reset zones are removed."

VEGETATION RESET: only selected vegetation is replaced. Eligible cells show green regenerated trees / copper and circular reset-arrow icons. Skipped blue cells retain their prior stumps / depleted ore. Central house remains solid in ALL SafeZones variants because ordinary player buildings are not directly targeted. In bottom outer row, second cell, draw ONE dashed orange terrain circle centered on a copper node; a line from copper center to circle edge is labelled "r". A leader below says "Optional terrain reset: radius r". The radius arrow is radial, NOT a diameter. Caption: "Selected vegetation regenerates." and "Player buildings are not directly deleted."

LOCATION RESET: selected stone dungeon entrances in eligible cells are regenerated, with green circular reset-arrow symbols near them. Trees and ore remain ordinary scenery, no whole-cell clearing. Mark the central base zone appropriately as specified below. Below the map include a compact enlarged single-zone detail: a dashed orange "Location radius" circle CENTERED ON A STONE DUNGEON ENTRANCE; a house beside the entrance lies entirely inside this circle and another identical house lies entirely outside the circle, BOTH on the SAME single zone tile. This is a magnification of the central base's zone, not a separate extra unprotected marker. House deletion/protection state is specified below. Caption: "Selected locations regenerate." and "Buildings inside the cleanup area can be removed."

Footer, exactly two short lines:
"Independent operations. Vegetation and location examples assume Zones = false."
"House = base with a configured marker. Skipped zones are not terrain barriers."

Do not imply that every arbitrary house is automatically a marker. Do not illustrate a step-by-step chain between columns. Do not show deletion symbols on any vegetation or on dungeon entrances. Protection boundaries are square zone grids, not a distance circle around the house. Keep labels concise and legible.

Title "FreshWorld — SafeZones = 0"; subtitle "No zone protection"; badge "0 zones skipped".
All 25 cells on all three maps are pale warm gold eligible; NO blue protected cell.
Zone map: healthy regenerated natural scenery everywhere, including the center. Central player house is a RED DASHED HOUSE silhouette plus small X, amongst regenerated natural scenery; it is NOT solid or rebuilt. Use green reset arrows on natural objects throughout.
Vegetation map: central solid house remains; green trees and copper regenerate in every eligible region, including center.
Location map: central stone entrance regenerates with circular reset arrows; beside it the central house inside the cleanup radius is a red dashed house + small X. This must not look like the whole zone was cleared. In the larger central-zone inset, INSIDE house is red dashed + X labelled "Inside: removed"; OUTSIDE house is solid labelled "Outside: retained". Circle centered on entrance, not house.
```

## SafeZones = 1

```text
Use case: infographic-diagram, educational game-mod illustration edit.
Image 1 is the old FreshWorld infographic: redraw its maps to fix the misleading permanent-clearing impression and simplify the icons, while retaining the clean three-column layout, warm off-white background, English labels, and precisely countable 5x5 square grids. Image 2 is the user's reset-symbol reference: two bold curved arrows chasing each other in a circle. Use that silhouette, recolored dark green, consistently to mean RESET / REGENERATE. This is not a delete symbol.

Landscape 1536x1024 or larger in the same 3:2 ratio. Crisp, restrained game-manual illustration with simple wood house, green tree, copper ore and stone dungeon entrance icons. Reduce decorative clutter. Exactly 5 rows and 5 columns per map, with row/column numbers 1–5; every grid cell boundary must remain clear.
Three headings: "ZONE RESET" / "zones_reset", "VEGETATION RESET" / "vegetation_reset", "LOCATION RESET" / "locations_reset".
Top legend: green circular arrows "Reset / regenerate", blue square "Skipped zones", wood HOUSE "Base with protection marker", red dashed HOUSE + small X "Player building removed". NO PORTALS, gates, separate marker objects, standalone marker poles or blue building types. Every solid house uses ONE identical simple brown house icon. The house is a schematic base containing a configured marker; it does not claim arbitrary pieces qualify in the game.

CRITICAL: ALL HOUSE SYMBOLS ON EACH MAIN MAP must be confined to row 3 column 3, the central cell. Remove ALL former houses from other cells, replacing them with tree/ore/dungeon icons. This ensures the depicted 1-cell / 3x3 protection areas are consistent; there are no unprotected bases elsewhere. The three columns independently show the same base position.

ZONE RESET: depict the FINAL REGENERATED NATURAL WORLD after normal world loading, NOT the temporary empty state. All eligible cells contain healthy regenerated green trees, ores and a few stone dungeon entrances, with green circular reset-arrow symbols distributed across the eligible area. NEVER use red dashed trees, tree X marks, empty cleared land, destroyed-tree icons or a forest disappearing. The green arrows mean natural vegetation / terrain / locations are reset. Player-built houses are NEVER regenerated. Protected blue cells show their unchanged world, with no reset arrows. Caption exactly: "Natural world regenerates on loading." and "Player buildings in reset zones are removed."

VEGETATION RESET: only selected vegetation is replaced. Eligible cells show green regenerated trees / copper and circular reset-arrow icons. Skipped blue cells retain their prior stumps / depleted ore. Central house remains solid in ALL SafeZones variants because ordinary player buildings are not directly targeted. In bottom outer row, second cell, draw ONE dashed orange terrain circle centered on a copper node; a line from copper center to circle edge is labelled "r". A leader below says "Optional terrain reset: radius r". The radius arrow is radial, NOT a diameter. Caption: "Selected vegetation regenerates." and "Player buildings are not directly deleted."

LOCATION RESET: selected stone dungeon entrances in eligible cells are regenerated, with green circular reset-arrow symbols near them. Trees and ore remain ordinary scenery, no whole-cell clearing. Mark the central base zone appropriately as specified below. Below the map include a compact enlarged single-zone detail: a dashed orange "Location radius" circle CENTERED ON A STONE DUNGEON ENTRANCE; a house beside the entrance lies entirely inside this circle and another identical house lies entirely outside the circle, BOTH on the SAME single zone tile. This is a magnification of the central base's zone, not a separate extra unprotected marker. House deletion/protection state is specified below. Caption: "Selected locations regenerate." and "Buildings inside the cleanup area can be removed."

Footer, exactly two short lines:
"Independent operations. Vegetation and location examples assume Zones = false."
"House = base with a configured marker. Skipped zones are not terrain barriers."

Do not imply that every arbitrary house is automatically a marker. Do not illustrate a step-by-step chain between columns. Do not show deletion symbols on any vegetation or on dungeon entrances. Protection boundaries are square zone grids, not a distance circle around the house. Keep labels concise and legible.

Title "FreshWorld — SafeZones = 1"; subtitle "Skip the base's own zone"; badge "1 zone skipped".
Exactly ONE central row3 column3 cell is blue in each of the three maps, a strong blue square border traces that one cell. Other 24 cells are pale warm gold eligible.
Zone map: healthy regenerated natural scenery and green circular reset arrows in all eligible outer cells. Central blue cell has one intact solid house and unchanged scenery, NO reset arrows.
Vegetation map: blue center keeps solid house with stumps/depleted ore and no reset arrows; outer eligible cells show regenerated green trees/copper and green reset arrows.
Location map: blue center contains the intact solid house and an unchanged small dungeon entrance, no green arrows. Eligible surrounding cells' dungeon entrances show green reset arrows. Central-zone inset is BLUE, both houses inside AND outside its location circle are intact with NO red X, no reset arrows; label "Base zone skipped: both buildings retained". Do not show removed houses in eligible cells because every house is a protection marker in this diagram.
```

## SafeZones = 2

```text
Use case: infographic-diagram, educational game-mod illustration edit.
Image 1 is the old FreshWorld infographic: redraw its maps to fix the misleading permanent-clearing impression and simplify the icons, while retaining the clean three-column layout, warm off-white background, English labels, and precisely countable 5x5 square grids. Image 2 is the user's reset-symbol reference: two bold curved arrows chasing each other in a circle. Use that silhouette, recolored dark green, consistently to mean RESET / REGENERATE. This is not a delete symbol.

Landscape 1536x1024 or larger in the same 3:2 ratio. Crisp, restrained game-manual illustration with simple wood house, green tree, copper ore and stone dungeon entrance icons. Reduce decorative clutter. Exactly 5 rows and 5 columns per map, with row/column numbers 1–5; every grid cell boundary must remain clear.
Three headings: "ZONE RESET" / "zones_reset", "VEGETATION RESET" / "vegetation_reset", "LOCATION RESET" / "locations_reset".
Top legend: green circular arrows "Reset / regenerate", blue square "Skipped zones", wood HOUSE "Base with protection marker", red dashed HOUSE + small X "Player building removed". NO PORTALS, gates, separate marker objects, standalone marker poles or blue building types. Every solid house uses ONE identical simple brown house icon. The house is a schematic base containing a configured marker; it does not claim arbitrary pieces qualify in the game.

CRITICAL: ALL HOUSE SYMBOLS ON EACH MAIN MAP must be confined to row 3 column 3, the central cell. Remove ALL former houses from other cells, replacing them with tree/ore/dungeon icons. This ensures the depicted 1-cell / 3x3 protection areas are consistent; there are no unprotected bases elsewhere. The three columns independently show the same base position.

ZONE RESET: depict the FINAL REGENERATED NATURAL WORLD after normal world loading, NOT the temporary empty state. All eligible cells contain healthy regenerated green trees, ores and a few stone dungeon entrances, with green circular reset-arrow symbols distributed across the eligible area. NEVER use red dashed trees, tree X marks, empty cleared land, destroyed-tree icons or a forest disappearing. The green arrows mean natural vegetation / terrain / locations are reset. Player-built houses are NEVER regenerated. Protected blue cells show their unchanged world, with no reset arrows. Caption exactly: "Natural world regenerates on loading." and "Player buildings in reset zones are removed."

VEGETATION RESET: only selected vegetation is replaced. Eligible cells show green regenerated trees / copper and circular reset-arrow icons. Skipped blue cells retain their prior stumps / depleted ore. Central house remains solid in ALL SafeZones variants because ordinary player buildings are not directly targeted. In bottom outer row, second cell, draw ONE dashed orange terrain circle centered on a copper node; a line from copper center to circle edge is labelled "r". A leader below says "Optional terrain reset: radius r". The radius arrow is radial, NOT a diameter. Caption: "Selected vegetation regenerates." and "Player buildings are not directly deleted."

LOCATION RESET: selected stone dungeon entrances in eligible cells are regenerated, with green circular reset-arrow symbols near them. Trees and ore remain ordinary scenery, no whole-cell clearing. Mark the central base zone appropriately as specified below. Below the map include a compact enlarged single-zone detail: a dashed orange "Location radius" circle CENTERED ON A STONE DUNGEON ENTRANCE; a house beside the entrance lies entirely inside this circle and another identical house lies entirely outside the circle, BOTH on the SAME single zone tile. This is a magnification of the central base's zone, not a separate extra unprotected marker. House deletion/protection state is specified below. Caption: "Selected locations regenerate." and "Buildings inside the cleanup area can be removed."

Footer, exactly two short lines:
"Independent operations. Vegetation and location examples assume Zones = false."
"House = base with a configured marker. Skipped zones are not terrain barriers."

Do not imply that every arbitrary house is automatically a marker. Do not illustrate a step-by-step chain between columns. Do not show deletion symbols on any vegetation or on dungeon entrances. Protection boundaries are square zone grids, not a distance circle around the house. Keep labels concise and legible.

Title "FreshWorld — SafeZones = 2"; subtitle "Skip the base's 3 x 3 zone neighborhood"; badge "9 zones skipped".
Exactly central rows2-4 and columns2-4 are blue in EACH map, a contiguous 3x3 square of NINE cells with internal grid lines. Thick blue perimeter surrounds the whole 9-cell block. Exactly16 eligible gold cells form the one-cell-wide outer ring.
Zone map: healthy regenerated natural scenery and green circular reset arrows throughout eligible outer ring. Blue9cells retain unchanged scenery, one solid central house and NO reset arrows.
Vegetation map: blue9cells keep stumps/depleted ore, central solid house, no reset arrows; outer16cells show regenerated trees/copper and green reset arrows.
Location map: blue9cells retain unchanged dungeon entrances; solid house is in central cell only; no reset arrows in any blue cell. Eligible outer-ring dungeon entrances have green reset arrows. Central-zone inset is BLUE, both houses inside AND outside location circle remain intact, no X or reset arrows; label "Base zone skipped: both buildings retained". Do not show removed houses in eligible cells because every house is a protection marker in this diagram.
```
