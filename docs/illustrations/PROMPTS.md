# FreshWorld illustration prompts

These prompts document the superseded first illustration set. See [REVISION-PROMPTS.md](REVISION-PROMPTS.md) for the current house and circular-arrow illustrations.

Generated with the built-in image generation tool. The supplied sketch was inspected as a conceptual reference; the images are new illustrations. Labels are English. No runtime code or release packaging changes are required.

## SafeZones = 0

```text
Use case: infographic-diagram.
Create a polished, accurate educational illustration for the Valheim mod FreshWorld. English text only. Landscape 2400x1600 or similar high resolution. Three equal spacious columns on warm off-white paper. Clean editorial vector-like game-manual illustration, lightly isometric 5 by 5 square zone grids, consistent viewpoint and icon design across columns. Thin crisp grid lines MUST make exactly 25 cells easy to count. Warm green low-poly vegetation, simple timber portal and wood-built houses, charcoal text, blue protected target cells, pale orange eligible cells, red dashed silhouettes with X marks for deleted objects. No real game screenshots, no characters, no UI controls, no brand logos.
A blue timber portal is a configured PLAYER-BUILT PROTECTION MARKER in the central cell; arbitrary houses do not create protection. All three columns depict the SAME original map independently and never a sequential workflow. Each cell is 64 x 64 m. Cells outside the 5x5 illustration are simply not shown.
At the top use the title specified below, a one-sentence subtitle, then a small legend: blue shield = "Skipped target zones"; pale orange = "Eligible target zones"; blue portal = "Protection marker"; red dashed object + X = "Removed".
Column headers exactly: "ZONE RESET" with small "zones_reset"; "VEGETATION RESET" with small "vegetation_reset"; "LOCATION RESET" with small "locations_reset".
ZONE column: eligible whole cells are shown cleared of trees/buildings, ready for later regeneration; use a few red dashed tree/house silhouettes with small X marks to indicate what was removed. Blue skipped cells retain their trees/houses. Portal is removed only if its zone is eligible. Under grid use: "Clears zone contents." and "World loading regenerates them later."
VEGETATION column: eligible cells show vivid regenerated selected trees and copper ore, while ordinary timber houses and the portal remain solid, intact. Blue skipped cells retain old stumps/depleted resources, without regeneration symbols. Around ONE regenerated copper node in an eligible cell show a small dashed amber terrain circle and a clearly marked radius arrow r, with callout "Optional terrain reset: radius r". The circle is NOT the base protection area. Under grid: "Replaces selected vegetation." and "Pieces are not directly deleted."
LOCATION column: show selected small stone dungeon entrances and surface cleanup circles wholly INSIDE their own eligible cell. A timber building INSIDE one eligible location circle appears red dashed with X; a timber building OUTSIDE that same circle in the SAME cell remains solid and intact. Make this distinction legible via a larger inset/callout diagram BELOW the grid if needed; its dashed circle must be labelled "Location radius". It is NOT full-cell clearing. Blue skipped cells retain their dungeon entrances and buildings with no reset arrows. Under grid: "Resets selected locations." and "Pieces inside the cleanup area can be removed."
Footer text, clearly readable, no extra prose: "Each column shows a separate operation. Resource and location examples assume Zones = false." Second line: "Protection skips target zones; it is not a terrain boundary. Surface cleanup is illustrated."
Do not depict the blue protected area as a circular distance shield. Do not say all buildings are markers, all pieces are always safe, or all terrain in blue cells is immutable. Omit dense technical paragraphs. Strong visual storytelling and precise grid geometry are more important than decorative detail.

TITLE: "FreshWorld — SafeZones = 0". SUBTITLE: "No marker-based protection."
All 25 cells in ALL THREE grids are pale orange eligible cells; there are NO blue skipped cells or blue outline protection rectangle. Show the original central portal in red dashed outline with X in ZONE grid, solid blue and intact in VEGETATION grid. LOCATION grid central cell IS eligible: put its solid blue portal safely outside the depicted location circle, one small house inside the circle removed with X, another small house outside the circle intact. The central location itself regenerates. Bottom location inset should clearly show "Inside radius: may be removed" and "Outside circle: not selected by this surface cleanup". In all panels no shield icons on cells because protection is disabled. Small header badge "0 zones skipped".
```

## SafeZones = 1

```text
Use case: infographic-diagram.
Create a polished, accurate educational illustration for the Valheim mod FreshWorld. English text only. Landscape 2400x1600 or similar high resolution. Three equal spacious columns on warm off-white paper. Clean editorial vector-like game-manual illustration, lightly isometric 5 by 5 square zone grids, consistent viewpoint and icon design across columns. Thin crisp grid lines MUST make exactly 25 cells easy to count. Warm green low-poly vegetation, simple timber portal and wood-built houses, charcoal text, blue protected target cells, pale orange eligible cells, red dashed silhouettes with X marks for deleted objects. No real game screenshots, no characters, no UI controls, no brand logos.
A blue timber portal is a configured PLAYER-BUILT PROTECTION MARKER in the central cell; arbitrary houses do not create protection. All three columns depict the SAME original map independently and never a sequential workflow. Each cell is 64 x 64 m. Cells outside the 5x5 illustration are simply not shown.
At the top use the title specified below, a one-sentence subtitle, then a small legend: blue shield = "Skipped target zones"; pale orange = "Eligible target zones"; blue portal = "Protection marker"; red dashed object + X = "Removed".
Column headers exactly: "ZONE RESET" with small "zones_reset"; "VEGETATION RESET" with small "vegetation_reset"; "LOCATION RESET" with small "locations_reset".
ZONE column: eligible whole cells are shown cleared of trees/buildings, ready for later regeneration; use a few red dashed tree/house silhouettes with small X marks to indicate what was removed. Blue skipped cells retain their trees/houses. Portal is removed only if its zone is eligible. Under grid use: "Clears zone contents." and "World loading regenerates them later."
VEGETATION column: eligible cells show vivid regenerated selected trees and copper ore, while ordinary timber houses and the portal remain solid, intact. Blue skipped cells retain old stumps/depleted resources, without regeneration symbols. Around ONE regenerated copper node in an eligible cell show a small dashed amber terrain circle and a clearly marked radius arrow r, with callout "Optional terrain reset: radius r". The circle is NOT the base protection area. Under grid: "Replaces selected vegetation." and "Pieces are not directly deleted."
LOCATION column: show selected small stone dungeon entrances and surface cleanup circles wholly INSIDE their own eligible cell. A timber building INSIDE one eligible location circle appears red dashed with X; a timber building OUTSIDE that same circle in the SAME cell remains solid and intact. Make this distinction legible via a larger inset/callout diagram BELOW the grid if needed; its dashed circle must be labelled "Location radius". It is NOT full-cell clearing. Blue skipped cells retain their dungeon entrances and buildings with no reset arrows. Under grid: "Resets selected locations." and "Pieces inside the cleanup area can be removed."
Footer text, clearly readable, no extra prose: "Each column shows a separate operation. Resource and location examples assume Zones = false." Second line: "Protection skips target zones; it is not a terrain boundary. Surface cleanup is illustrated."
Do not depict the blue protected area as a circular distance shield. Do not say all buildings are markers, all pieces are always safe, or all terrain in blue cells is immutable. Omit dense technical paragraphs. Strong visual storytelling and precise grid geometry are more important than decorative detail.

TITLE: "FreshWorld — SafeZones = 1". SUBTITLE: "Skip the marker's own zone."
In EACH 5x5 grid, ONLY the SINGLE CENTRAL CELL is muted blue, with a bold blue square outline tracing that one cell and a small shield. The remaining 24 cells are pale orange eligible. The central blue portal remains solid in all columns. In ZONE grid central cell remains wooded/built, outer cells cleared. In VEGETATION grid central blue cell keeps old stumps/depleted ore, eligible outside cells have bright regenerated trees/ore; ordinary houses survive everywhere. In LOCATION grid central blue cell's dungeon entrance/buildings remain unchanged, and eligible outer cells' selected dungeon entrances regenerate with local circular cleanup. Put inset example from one eligible outer cell showing a removed house inside its location circle and an intact house outside it. Small header badge "1 zone skipped".
```

## SafeZones = 2

```text
Use case: infographic-diagram.
Create a polished, accurate educational illustration for the Valheim mod FreshWorld. English text only. Landscape 2400x1600 or similar high resolution. Three equal spacious columns on warm off-white paper. Clean editorial vector-like game-manual illustration, lightly isometric 5 by 5 square zone grids, consistent viewpoint and icon design across columns. Thin crisp grid lines MUST make exactly 25 cells easy to count. Warm green low-poly vegetation, simple timber portal and wood-built houses, charcoal text, blue protected target cells, pale orange eligible cells, red dashed silhouettes with X marks for deleted objects. No real game screenshots, no characters, no UI controls, no brand logos.
A blue timber portal is a configured PLAYER-BUILT PROTECTION MARKER in the central cell; arbitrary houses do not create protection. All three columns depict the SAME original map independently and never a sequential workflow. Each cell is 64 x 64 m. Cells outside the 5x5 illustration are simply not shown.
At the top use the title specified below, a one-sentence subtitle, then a small legend: blue shield = "Skipped target zones"; pale orange = "Eligible target zones"; blue portal = "Protection marker"; red dashed object + X = "Removed".
Column headers exactly: "ZONE RESET" with small "zones_reset"; "VEGETATION RESET" with small "vegetation_reset"; "LOCATION RESET" with small "locations_reset".
ZONE column: eligible whole cells are shown cleared of trees/buildings, ready for later regeneration; use a few red dashed tree/house silhouettes with small X marks to indicate what was removed. Blue skipped cells retain their trees/houses. Portal is removed only if its zone is eligible. Under grid use: "Clears zone contents." and "World loading regenerates them later."
VEGETATION column: eligible cells show vivid regenerated selected trees and copper ore, while ordinary timber houses and the portal remain solid, intact. Blue skipped cells retain old stumps/depleted resources, without regeneration symbols. Around ONE regenerated copper node in an eligible cell show a small dashed amber terrain circle and a clearly marked radius arrow r, with callout "Optional terrain reset: radius r". The circle is NOT the base protection area. Under grid: "Replaces selected vegetation." and "Pieces are not directly deleted."
LOCATION column: show selected small stone dungeon entrances and surface cleanup circles wholly INSIDE their own eligible cell. A timber building INSIDE one eligible location circle appears red dashed with X; a timber building OUTSIDE that same circle in the SAME cell remains solid and intact. Make this distinction legible via a larger inset/callout diagram BELOW the grid if needed; its dashed circle must be labelled "Location radius". It is NOT full-cell clearing. Blue skipped cells retain their dungeon entrances and buildings with no reset arrows. Under grid: "Resets selected locations." and "Pieces inside the cleanup area can be removed."
Footer text, clearly readable, no extra prose: "Each column shows a separate operation. Resource and location examples assume Zones = false." Second line: "Protection skips target zones; it is not a terrain boundary. Surface cleanup is illustrated."
Do not depict the blue protected area as a circular distance shield. Do not say all buildings are markers, all pieces are always safe, or all terrain in blue cells is immutable. Omit dense technical paragraphs. Strong visual storytelling and precise grid geometry are more important than decorative detail.

TITLE: "FreshWorld — SafeZones = 2". SUBTITLE: "Skip the marker's 3 x 3 zone neighborhood."
In EACH 5x5 grid, EXACTLY the central 3 by 3 square of NINE cells is muted blue with visible internal grid lines. Bold blue square outline surrounds ALL NINE CELLS together, NOT just center. ONE-CELL-WIDE OUTER RING of 16 cells is pale orange eligible. Central blue portal remains solid. In ZONE grid all nine blue cells retain trees/houses, outer ring cells are cleared. In VEGETATION grid all nine blue cells keep old stumps/depleted ore while the 16 outer ring cells get regenerated resources; ordinary houses survive everywhere. In LOCATION grid nine blue cells' dungeon entrances/buildings remain unchanged, eligible outer ring selected locations regenerate with local circular cleanup only. A small inset from an eligible outer cell shows a house inside its circle removed and a house outside its circle intact. Small header badge "9 zones skipped".
```
## Final refinement prompts

These edits correct the generated zone-clearing and radius annotations. Image copies retain the generated pixels; no manual raster edits were applied.

### SafeZones = 0

```text
Edit this infographic into the matching SafeZones = 0 companion page. Preserve its clean off-white layout, three column widths, English typography, icon style, coordinate numbers 1-5, and EXACT 5x5 cell grids in ALL THREE columns.
Change title to "FreshWorld — SafeZones = 0", subtitle to "No marker-based protection.", and top-right badge to "0 zones skipped" with a no-protection symbol.
Remove ALL blue skipped-cell fills, blue cell borders and cell shield icons from all three maps. Every cell is eligible pale orange; the portal icon remains blue where it survives.
CRITICAL ZONE RESET panel: ALL 25 cells must show cleared bare terrain, with no surviving solid trees, houses, dungeon entrances, rocks OR STUMPS. Keep a few red dashed silhouettes with X signs as removal symbols. Central portal must also be a red dashed silhouette with X. Bare terrain is plain textured ground. This is cleared state BEFORE regeneration on later world loading.
VEGETATION RESET panel: preserve ordinary solid timber houses and the solid central portal. Show regenerated green trees and copper including the newly eligible center cell. Do not use red X on player buildings here. Preserve one correctly labelled optional terrain circle centered on regenerated copper, with radius r.
LOCATION RESET panel: all cells eligible, including center. The central portal and one house remain OUTSIDE the central dungeon's surface cleanup circle; a small timber house INSIDE that circle has a red dashed outline and X. Do not clear the whole cell.
CRITICAL bottom-right inset: the dashed "Location radius" circle must be geometrically CENTERED ON THE STONE DUNGEON ENTRANCE, not centered on the red house. Inside that circle next to the entrance, show a removed red dashed house with X. Beyond the circle show an intact timber house, still on the SAME illustrated terrain tile. Preserve existing inset legend and bottom captions.
Preserve these exact footer sentences: "Each column shows a separate operation. Resource and location examples assume Zones = false." and "Protection skips target zones; it is not a terrain boundary. Surface cleanup is illustrated."
Do not add decorative scenery or additional explanatory paragraphs. English only. No untouched objects or stumps in the ZONE grid. Preserve the numbered 5 by 5 geometry.
```

### SafeZones = 1

```text
Make only three precise corrections to this SafeZones = 1 infographic. Keep the title, all text, layout, colors, coordinate labels 1-5, icons and EXACT 5x5 grids. Preserve exactly ONE central blue protected cell in EACH grid and the badge "1 zone skipped".
1. ZONE RESET grid: remove EVERY SOLID STUMP, rock, debris and other remaining physical object from all 24 pale-orange cells. They must be plain bare terrain with only the existing RED DASHED removal silhouettes / X marks. The central BLUE cell alone retains its solid portal, trees and house. Do not turn stumps into new trees. The zone is completely cleared before later regeneration.
2. LOCATION RESET grid: add a small unchanged stone dungeon entrance beside the existing blue portal INSIDE the central BLUE cell, so viewers can see that the location itself is skipped. Keep it fully inside that one cell; no reset arrow or cleanup ring in this blue cell.
3. Bottom-right "Location radius" inset: redraw only the arrangement of entrance/circle/houses so the dashed circle is geometrically CENTERED ON THE STONE DUNGEON ENTRANCE. A removed red dashed house with X sits inside this entrance-centered circle, beside the entrance. A solid intact timber house sits outside this circle but on the same terrain tile. The current incorrect circle centered only on the red house must be fixed. Preserve the inset legend and all labels.
Do not change the vegetation panel or turn square blue zone protection into circles. No extra text. English only.
```

### SafeZones = 2

```text
Make only the following correctness edits to this SafeZones = 2 infographic. Preserve the English title, legend, clean layout, grid geometry, column labels, and footer. Each map MUST keep exactly a 5x5 grid with a central 3x3 block of NINE blue skipped cells and 16 orange outer-ring cells. Do not change any protection boundary.
1. ZONE RESET map: remove ALL solid stumps, debris and rocks from the 16 eligible OUTER-RING cells. They are completely cleared bare ground with only red dashed removal silhouettes/X marks. Preserve all solid portal/trees/houses inside the nine blue cells.
2. LOCATION RESET map: where an eligible cell contains a dashed cleanup circle with a red removed house or tree, include a small stone dungeon entrance AT THAT CIRCLE'S CENTER. Cleanup circles are centered on locations, never arbitrary houses or trees. Do not add any circles or deletion markers inside the nine blue protected cells.
3. Bottom-right inset: add a distinct stone dungeon entrance and center the dashed "Location radius" circle on that entrance. Place a red dashed house with X inside the same circle beside the entrance, and a solid timber house outside the circle on the same terrain tile. Change inset labels to "Inside radius: may be removed" and "Outside circle: not selected by surface cleanup".
No new columns or paragraphs. Do not put living trees back into the cleared outer zone ring. Preserve the vegetation panel and all nine blue protected cells.
```

### SafeZones = 1: final terrain-radius annotation

```text
Edit only the optional terrain-radius annotation in the middle VEGETATION RESET column. Keep EVERY OTHER PIXEL/element as close to the original as possible: title SafeZones=1, three 5x5 grids, exactly one blue center cell in each, zone cleared cells, location inset, footer, colors and all text.
In the VEGETATION grid's bottom row, SECOND cell from the left, draw a thin dashed amber ellipse on the ground, centered on the existing copper ore node. Keep the ellipse wholly within this one cell. Add a small black radius arrow from the ore center to the ellipse's rim, label it "r". Connect the existing text box "Optional terrain reset: radius r" below the grid to that ellipse with a thin amber leader line. This circle is around the ORE and must not encircle the blue portal or center protected cell.
Do not change or remove any existing objects, numbers, blue squares, legend items, column labels, or the location cleanup inset. This is one local annotation correction only.
```
