# Dream Game: design doc

Working title. This is the scope we agreed on before writing any code. It changes as we playtest.

## Pitch

A military-industrial sandbox. You design everything yourself, from a cannon's breech to a refinery to a capital ship. Then you fight a war over a randomly generated star system: on foot, from the driver's seat, and from orbit.

## Why this game exists

It's built to fix what went wrong in the games that came closest:

| Game | What went wrong | What we do instead |
|---|---|---|
| From the Depths | Too many menus, unclear blocks, clumsy controls and camera | A builder with one screen, live stats and warnings, and a comfortable camera |
| Stormworks | The learning curve: you had to watch tutorials to get started | Deep systems that run automatically by default, opening missions that teach by doing, and example designs |
| Space Engineers 1 | Nothing pulled you along | A conquest campaign with factions that push back and territory worth taking |

## Pillars

These three have to feel great, or the game fails:

1. **Building.** Fast, clear and powerful. Everything is custom.
2. **Destruction.** Every block can break. Armor shatters, and pieces fall away with physics.
3. **The war.** The campaign feels alive: fronts move, and your factories and supply lines matter.

## Decisions

| Area | Decision |
|---|---|
| Platform | Downloadable PC game in Godot 4 (C#). Target hardware: a gaming PC |
| World | A newly generated star system each campaign. Ground and space are separate battlefields, joined by a system map |
| Fronts | On a planet you fight on land, at sea (ships and submarines) and in the air (planes, helicopters, VTOL and drones, airships). All of them are built in the same builder from the same blocks |
| Machine size | From scout cars up to colossal machines 300 m and longer: land battleships, carriers, mobile fortresses |
| Player | A soldier-engineer on foot with full shooter combat. No hunger, thirst or oxygen meters; only health |
| Building | Block by block, plus reusable parts you've designed (like a turret you drop onto any hull). Everything is custom: guns are built from components, and factory machines are built from blocks |
| Damage | Every block has health and armor. Pieces that lose their connection break off as physics debris |
| Physics | Realistic but forgiving: mass, balance and recoil matter, and getting unstuck or flipped back over is easy |
| Vehicle systems | Fuel, power, ammo feed, heat, crew and oxygen. Automatic by default, hands-on if you want |
| Automation | Ready-made orders (guard, patrol, escort, attack), plus an optional visual logic editor |
| Industry | Machines you design, 15–25+ resources, prototypes welded by hand, then mass production in an assembly bay |
| Logistics | A supply network: depots linked by roads and rail that you build. Supplies flow along it automatically, and routes can be seen and attacked |
| Terrain | Fully deformable: craters, trenches, tunnels, mining |
| Campaign | Conquest against 1–5 rival factions (chosen at setup), each with a hostility meter. Losses are setbacks; you're only out when your last base falls |
| Research | Research labs, salvaged enemy wrecks and captured sites |
| Space | You command capital ships. Holding orbit gives you orbital strikes, landings, scouting and supply between planets |
| Battle size | Massive: 100+ vehicles per side, at full detail near players and simplified far away |
| Multiplayer | Solo first. Then a 2–4 player co-op campaign and a PvP mode. Friends can take crew seats, and AI fills the empty ones |
| Teaching | Opening missions teach by doing, example designs you can open up, and live stats and warnings in the builder |
| Look and tone | Realistic-ish. Heavy and grounded, with over-the-top weapons |
| Sharing | Blueprint files now, Steam Workshop later |
| Goal | Steam eventually. A game for you and your friends is also a fine outcome |

## Systems

### Builder
- One screen. A block bar at the bottom, a stats panel on the side, and nothing buried in menus.
- Camera: right-drag orbits, middle-drag pans, the wheel zooms.
- A ghost preview shows where the next block will go: green if it fits, red if it doesn't.
- Mirror mode builds both sides at once.
- The stats panel updates live: mass, center of mass, power-to-weight, top speed, and armor on each side. It also shows each gun's caliber, muzzle velocity, penetration and reload.
- Warnings say exactly what's wrong and why, such as "Gun has no ammo connected" or "Top-heavy: will tip on steep slopes".
- Blueprints are versioned JSON files, so friends can share them as files.
- **Box tools** (B cycles them): drag out a box to fill it with the selected block, build just its shell (walls, floor and roof, so a hull goes up in one go), or clear everything inside it. The mouse wheel makes the box taller while dragging. Big blocks tile the box. Mirror mode applies.
- The build area is 320 m across and 80 m high, big enough for a 300 m battleship.
- **Designs should look like machines, not stacks of cubes.**
  - Plain armor, frames and gas cells merge into continuous surfaces: a wall of blocks reads as one plate. Only outside edges are chamfered, corners and creases are shaded darker, paint is weathered in soft blotches, worn lighter along the edges, and each block's panel is tinted very slightly so plating still shows.
  - Everything else is a detailed model: tracks with links and road wheels, treaded tires with bolted hubs, engines with cooling fins and exhausts, breeches with recoil cylinders, racks of shells, twisted propeller and screw blades, turbines, stacked lead ingots.
  - The dashboard groups guns that are built the same ("406 mm × 12"), so a big battery stays readable.
- **Shapes.** Armor (light, heavy, composite), airframe and glazing come in ten shapes, picked with V or from the part card: cube, slope, shallow slope (a low half and a high half, two blocks for one long slope half as steep), outer corner, inner corner, rounded edge, half block, plate (a quarter cell thick: skirts, shields, fins) and octagon (pipes, funnels, round hulls). A shaped block weighs and holds as much as the steel it has, keeps its plate's thickness, and counts as thicker when hit at an angle: a shallow two-block glacis reads as about twice its plate head-on. Shaped plating merges into the hull surface like the cubes, so a slope and the plates around it are one surface. Every shape is defined once (convex pieces cut by planes, `src/Core/Shapes.cs`) and shells, colliders, meshes and the hull surface all use that one definition.
- **Paint jobs.** A design can wear a paint job, picked in the builder's top bar and saved with the blueprint: factory colors, olive, woodland, desert, arctic, urban digital, tiger stripes, naval grey, dazzle, night and sky. Plating is painted point by point in the pattern, with seams between plates every meter and grime streaks down the sides; smaller parts take the pattern's color where they are; steel, rubber, glass and lamps stay as they are. Ships get ship rules on top of any paint job: red anti-fouling below the waterline, a black stripe along it and grey decks. A ship with no paint job wears naval grey. The pattern math lives in the core (`Liveries.ColorAt`) and the shader copies it exactly, so far models and previews match.
- **Glazing and cockpits.** Glazing is framed glass: a pane per cell, with frame bars along the edges, in every armor shape. The pilot seat is an open cockpit (a tub, a seat, a stick, lit instruments) made to sit under a canopy of glazing. Glazing is light (16 kg a block) and stops splinters, not shells.
- **Details** (their own category) are for looks only: hatch, periscope, headlight, stowage box, spare track links, antenna, railing, ladder, smoke launchers, exhaust, searchlight, vent grille, grab handles, stowage rack. Each sits on the bottom of its cell (or against the back), so turn it to put it on any face. They weigh a few kilos and have no collider.
- **Joints.** Parts show how they're attached, worked out from their neighbors: an axle from every wheel to whatever it hangs from, and a collar where every gun barrel leaves its breech. (`src/Core/Joints.cs`.)
- **Copy and paste.** Ctrl+C (or the box tool's Copy mode) and drag out a box: everything in it goes on the clipboard. Then click to paste it anywhere; R, T and G turn it, H flips it left to right, Esc or a right click stops. Mirror mode pastes both sides. "Save part" keeps the clipboard as a part file; Load, Parts brings it back to paste into any design.
- **Armor view** (O, or the ARMOR button): every block colored by its armor thickness, from grey (under 10 mm) through blue, green, yellow and orange to red (200 mm and up), with a legend. Parts that aren't armor go dark.

### Fleet command
Command the units you aren't driving yourself, from a command ship, a command vehicle, or a ground post.
- **The command console** (Control) is screens and radios. From a vehicle with a working one, or standing at a command post (one stands by the depot), M opens the **tactical map**. The Corsair, the Zephyr airship and the Colossus carry one.
- **The tactical map** is a top-down view of the battle that keeps running while it's open. Your units are blue (yours gold); an enemy shows only while one of your units can see it: tanks see about 1 km, ships 2.5 km, aircraft 3 km, more with a **search radar** (Control) aboard, and hills block the view. One you've lost sight of stays faded where it was last seen, for a minute. Each unit has a health bar and a line showing what it's doing.
- **Picking units**: click one, drag a box round several, Shift adds. Ctrl+1-9 saves the pick as a group, 1-9 picks it again (Shift+number also centers on it).
- **Orders** come from a right click: on an enemy, **attack** it (they close in and keep every gun on it); on one of yours, **escort** it; on open ground or sea, **move** there in the chosen formation (line abreast, wedge, column or box; F cycles). Shift+right click **patrols** back and forth. H **holds**. Ships only go to water, land units only to land; aircraft circle over wherever they're sent.
- **Rules of engagement**: fire at will, return fire (only at what's shooting at them, or what they were told to attack), or hold fire.
- **Command range**: orders reach 4 km from the console. Units further out keep their last orders. If the console is destroyed, the map closes and everyone carries on with what they were last told.
- **Jump in** (J) takes over the first unit picked: you move into its driver's seat. When you leave, it goes back to its orders.
- **Deploy** calls in more units from the map: any example or your own saved design, on your side or the enemy's, placed where you click.
- **Seeing what's going on:**
  - Every unit you command has a **callsign** ("Warden 2", numbered per design as they arrive) and says what it's doing in plain words: "moving · 320 m to go", "closing on enemy Bulwark · 1.2 km", "attacking enemy Warden", "firing at enemy Jackal", "in position", "escorting Corsair 1", "can't get through", "knocked out", plus "under fire".
  - On the map each unit carries a status icon (moving, in position, closing in, firing, escorting, patrolling, stuck, knocked out), an orange ring while it fires and a pulsing red ring while it's being fired on. Orders show as lines with arrowheads to a marker where it's going (amber when it's stuck) or brackets round its target. Giving an order pulses where you gave it, and the confirmation names the units and how far they have to go.
  - **Unit reports** (bottom left of the map; in the message feed, marked ◂, while the map is shut): in position, no target left, lost the unit it was escorting, under fire (once per spell), damaged (below 60%) and badly damaged (below 30%), knocked out, knocked out an enemy, can't get through (no closer to where it's going for about 10 seconds, not counting stops to shoot) and moving again. Reports of the same kind that come in together are rolled into one line ("Warden 1, Warden 2 and 3 more in position").
  - With nothing picked, the map's panel sums up all your units: how many are moving, in position, firing, closing in, waiting, stuck or under fire.
  - In the 3D view, units you've given orders to carry a label over their marker with their callsign, status icon and what they're doing (out to 900 m). Markers and labels stay the same small size on screen however far off the unit is (the diamond about 3% of the screen's height, each line of text about 2%).
  - On the map each unit is drawn at its real size from above (a ship's outline, a building's footprint), but never smaller than an icon you can click.
- What's next for command: zones and objectives to fight over, enemy commanders that give their own orders, and supply.

### The island's ground and growth
- **Ground.** One shader paints the island from layers each point of the ground carries:
  - **Grass:** green and dry patches, a speckle of blades, the odd bare spot.
  - **Dirt:** pebbly, on the firing lane and higher up.
  - **Rock:** with strata and cracks on the slopes.
  - **Sand:** at the shore, with a dark wet band where the sea reaches it, then the seabed.
  - **Churned earth:** dark soil with clods wherever shells landed or someone dug.

  All of it comes from noise in world space, so nothing tiles or needs image files.
- **Trees, bushes and rocks.**
  - **Where they grow:** woods in clumps (pines higher up, broadleaf lower down), bushes round their edges and scattered in the open, rocks and boulders on the slopes. Nothing on the beach, the firing lane, the airstrip, the helipad or the building pads. The placement is in the rules code and seeded, so every player's island is the same.
  - **Models:** made in Blender (`tools/blender/env_library.py`, to `models/env.glb`): trunks with bark ridges, leaves on cards that the shader cuts into clusters and sways in the wind, rocks with lichen.
  - **Drawing:** in batches per kind per 64 m patch; bushes stop being drawn at 260 m, small rocks at 420 m, trees at 1.1 km.
  - **Getting knocked down:** trunks and rocks are solid. A vehicle hitting a tree hard enough (mass × speed over about 18 tonne·m/s for a full-size tree: a tank at walking pace, not a light car) knocks it flat the way it was going. Bushes go flat under anything that drives over them. Blasts throw trees down away from them, and a tree whose ground is blown away falls over.

- **The village** south-east of the lane, on ground levelled for it, joined to the lane by a dirt road that becomes its main street:
  - two brick houses (one with timber gables), a brick cottage and a timber cottage, facing each other across the street;
  - a timber barn at the north end;
  - a brick chapel with a square tower by the crossroads;
  - low brick garden walls along the street.

  They're built from three new structure blocks:
  - **Brick:** stops bullets, not shells; blast knocks it down.
  - **Timber:** light, weak.
  - **Roof tiles:** for the slopes.

  Each comes in every shape. The paint shader draws them without textures (brick courses with mortar, each brick its own tone; boards with grain and seams; rounded tiles in rows) and weathers them with grime and mud, not chipped paint. The buildings have doors, windows, upper floors, gable roofs and chimneys, and fall apart like anything else built from blocks. Trees keep off the village and the roads.
- **Ruts.** Wheels and tracks wear the grass under them down to dirt, and on to mud, wherever they drive within sight (600 m of the camera). Tracks wear more than tyres, and heavy machines more than light ones. One tank pass leaves a faint trail; a busy route becomes a dirt track. Wear only changes how the ground looks, so it just redraws its patch, without reshaping the ground or waking vehicles. It's in the rules code with the craters.
- **Dust and mud.** Moving wheels and tracks throw up a low cloud of dust on dry ground, and clods of mud when it's raining or the ground's rutted, from the back of each side, close to the camera.
- **The sea.** Fine ripples (two layers of a normal map drifting different ways) over the rolling swell; surf rolling in over the shallows in foam bands; whitecaps on the crests when the weather makes it rough.
- **Time of day and weather** (F6, F7 on the range; blended over a few seconds).
  - **Sky:** a sky of its own, with a gradient, a glow round the sun, clouds that drift and are lit from the sun's side, stars at night, and the far sea in haze below the horizon.
  - **Times:** morning (low warm sun from the east), noon, evening (low orange sun in the west) and night (a dim blue moon, stars, dark haze; headlights and lamps matter).
  - **Weather:**
    - **Clear.**
    - **Overcast:** grey sky, weak sun, more haze, more wind and swell.
    - **Rain:** streaks falling round the camera. The ground, paint and leaves get wet over about twenty seconds (darker and shinier) and dry over a minute. Strong wind in the trees and a rough sea.
    - **Fog:** thick haze, still air, a flat sea.
  - **Shared settings:** wetness, wind and sea state are shared shader settings that the ground, paint, trees and sea all read. Benchmark runs take `--time=` and `--weather=`.

### Style rules
What every part should follow, so a design reads as one machine however odd it is:
1. **Flat faces, chamfered edges.** Curves only where something turns or holds pressure: wheels, barrels, tanks, rotors, round hulls.
2. **Glass is flat panes in frames,** never a bubble.
3. **Detail everywhere, at about the same density.** Plating gets panel seams and grime; parts get bolts, rims, hinges and handles; nothing large is featureless.
4. **Parts show how they attach:** axles, collars, mounts, shafts.
5. **Muted, real-world colors** for structure; bright colors only for lamps, markings and paint jobs.
6. **People-sized cues** (hatches, ladders, railings, handles) so the scale reads.
7. **Gritty realism** (chosen from a three-way look-test: gritty, clean and stylised, modern military). Machines look used:
   - **Chipped paint:** it wears through on the chamfered edges first, down to red-brown primer and then bare steel, with a scatter of chips elsewhere.
   - **Grime** in the creases, uneven and patchy.
   - **Rust** streaking down the sides from edges and fittings.
   - **Sun-bleached** tops.
   - **Mud** caked low down and splashed higher.

   **Part library (Blender).** Detailed meshes for the most visible parts are made by a Blender script (`tools/blender/parts_library.py`, run with `blender -b -P tools/blender/parts_library.py -- models/parts.glb`) and exported to `models/parts.glb`: so far:
   - **Running gear:** tyres with chevron tread, pressed-steel rims, hubs with wheel nuts, road wheels with lightening holes, cast track shoes with grousers and pins (also used for spare tracks).
   - **Guns:** double-baffle muzzle brakes; breech blocks with a breech ring, recoil cylinders, trunnions and an operating lever.
   - **Fittings:** cast round hatches with a hinge, handle and bolted ring; periscope heads with a rain hood; headlight buckets; stowage bins with lids and bands; flared smoke-grenade cups; exhaust silencers.
   - **Aircraft:** whole twisted propeller blades; helicopter rotor blades with a root cuff and tip cap; jet engine casings with bands and stringers, and petal nozzles.

   A library mesh can replace several simple pieces at once (a blade's two halves, a hatch and its hinge), fitted to a box of its own. Block models still describe every part as a simple shape, and can tag it with a library mesh's name. When the library's there, the detailed mesh is drawn in its place, fitted to the shape's size, and detail the mesh already has is left out; without it, the simple shapes are drawn. Godot makes each mesh's detail levels when it imports the file. Far models always use the simple shapes. The meshes mark their bevels for the weathering. A test checks that every name a block asks for is in the library.

   Bare metal (barrels, tracks) only rusts and gets dirty; rubber and concrete only get dirty. How worn and how muddy is set per machine: tanks are the muddiest, aircraft barely muddy, ships never (anti-fouling below the waterline), buildings a little dusty. It's all one shader on top of the paint job: the meshes mark their chamfers, and the wear is cut from a seamless noise texture, so it costs no extra draw calls or files.

### Guns (built from components)
A gun is a **breech** with **barrel** segments in a line in front of it. Parts attached around the breech change how it performs:

| Part | Effect |
|---|---|
| Breech (37 / 76 / 120 mm) | Sets the caliber, which sets shell mass, base damage and base reload |
| Barrel segment | More muzzle velocity and penetration, with diminishing returns. Heavier, and slower to turn |
| Muzzle brake | Less recoil |
| Propellant charge | More velocity and recoil (up to 2 count) |
| Loader | Faster reload (up to 3 count) |
| Ammo rack (AP / HE) | Holds shells. It has to connect to the breech, directly or through feed blocks. If it's destroyed while loaded, it explodes |
| Ammo feed | Carries ammo between racks and the breech |

Guns inside a turret group traverse (turn left and right); every gun elevates.

### Modern and future weapons
The newer weapons are built the same way as guns: a breech with its own kind of barrel in a line in front of it. Ordinary barrels only fit ordinary breeches, so the builder says so if you mix them up.

| Weapon | Breech + "barrel" | How it works |
|---|---|---|
| **Railgun** | Railgun Breech + Rail Segments | A 40 mm slug at about 2.4 km/s with eight rails: flat, fast, and harder-hitting than a 120 mm gun. Slugs come from AP racks. Every shot takes 20 MJ of electricity. |
| **Laser** | Laser Emitter + Focusing Lenses | Ten pulses a second, each burning the first thing the beam touches. No drop, no lead, no ammo. Armor slows the burn but never stops it (heavy plate takes several seconds). Full strength out to about 1.3 km with two lenses, fading beyond. Takes 5 MW while it fires. |
| **CIWS** | 20 mm Gatling + barrels | Bursts of five rounds, fifty a second, from HE racks. |
| **Rocket artillery** | Rocket Launcher + Launch Tubes | Each tube pack adds two rockets to a salvo, rippled off five a second. Unguided, high explosive, reaching several kilometers. Rockets come from HE racks. |

**Point defense.** The CIWS and the laser are point-defense weapons: their gunners shoot at missiles and bombs coming at their own vehicle before anything else, with no settling-in time (radar aims them). A missile or bomb takes a few hits to knock down, more for big ones. Point defense only follows the player's aim when the player sits in its own mount.

**Vertical launch cells.** A VLS cell is a deck cell with a missile inside, fired straight up through its hatch (which must stand upright and be clear). The **strike missile** is a cruise missile: it climbs out, cruises at a height that clears the ground, and dives onto the spot under the crosshair when it was fired, up to about 12 km away, with a warhead the size of a bomb load. Fired at a vehicle, it keeps tracking it. The **air-defense missile** locks on to the enemy aircraft nearest the crosshair (out to 6 km) and runs it down. AI crews fire air-defense missiles at aircraft and strike missiles at targets from 400 m out. Supply points reload the cells. A loaded cell that's hit takes its warhead with it.

### Power
Energy weapons run on electricity. **Generators** (1.5 MW, burning fuel while they charge) and the **Fusion Reactor** (3×3×3, 40 MW, no fuel) make it; **Capacitor Banks** store it (12 MJ each), and each generator or reactor holds about a second of its own output too. Railguns and lasers take each shot's charge from the store; if it isn't there, they wait ("CHARGING" on the HUD). The builder shows what's made, what's stored, and whether the energy weapons can fire flat out on it. Losing a generator or capacitor to damage loses what it made or held. A destroyed reactor goes up in an enormous blast.

### Big booms
Blasts of 7 m and up (reactors, strike missiles, magazines) get the full treatment: a blinding flash, a fireball that swells and rises, a column of smoke climbing into a cap, a shock ring and a ring of dust racing out along the ground, burning debris, and a wide scorched crater that lasts. The shock wave shoves every body within two and a half times the blast radius, hurt or not, and the camera shakes with it.

### Jets
Jets push on their own at any speed. Opening the throttle all the way lights the **afterburners**: 60% more thrust for three and a half times the fuel, a long bright flame and a note on the HUD. Every jet shows a flame that grows with the throttle. The **Falcon** is the example jet fighter: swept wings with strakes along the nose, two jets in the tail, wing-root cannon and radar missiles.

### Damage
- **AP rounds** trace through the block grid. Each block's armor is scaled by the impact angle (`thickness / cos(angle)`). If penetration is greater, the block takes damage and the shell keeps going with the remaining penetration. At steep angles, shells ricochet.
- **HE rounds** explode on impact. Damage falls off with distance, and armor reduces it.
- When blocks are destroyed, the grid checks what's still connected. Pieces cut off from the main body become separate physics debris.
- Losing key blocks has effects: no driver means the vehicle is disabled, no engine means no power, and a destroyed ammo rack can cook off.

### Crew
- Three seat types: **Driver** (drives), **Gunner** (aims and fires the guns in its turret, or hull guns from the hull) and **Commander** (high-zoom periscope; AI gunners lock on faster and steadier with a commander aboard).
- Each gun is fired by a gunner in the same turret, otherwise the nearest gunner anywhere, otherwise the driver. A design with only a driver seat still works: the driver aims everything.
- Every seat starts with an AI crew member. The player takes over a seat (1/2/3 switch seats). With the player aboard, the AI gunner **follows the player's aim** by default; **weapons free** (G) lets it pick its own targets.
- AI gunners pick the nearest visible enemy (vehicles first, people if much closer), lead moving targets, start a little off and settle in, and choose AP for vehicles, HE for people.
- A destroyed seat takes its crew member with it. If it's the player's seat, the player dies.

### On foot
- **First person.** It gives a proper shooter feel without needing an animated character model yet.
- Weapons: **battle rifle** (automatic, lethal to people, sparks off armor) and **AT rocket** (slow, drops, about 280 mm shaped charge that blows up inside). Both use the same shells and damage rules as vehicle guns. Customizable infantry weapons come later, built from parts like vehicle guns are.
- **Welding torch:** repairs damaged blocks and rebuilds destroyed ones exactly as designed (shown as blue outlines while the torch is out). A block can only be rebuilt where it attaches to something, so you rebuild from the inside out. Rebuilt ammo racks start empty.
- **Supply points** rearm and refuel parked vehicles, and heal and restock people. This is the seed of the logistics network.

### Interiors
- **Walking on moving vehicles.** Standing on a vehicle, the player moves in its frame: the deck carries them as it moves, rolls and turns (and turns them with it), and their walking is relative to the deck. Stepping or jumping off, they keep its speed.
- **Crew stations.** On foot, E next to a seat takes that exact seat (walk to the gun you want to man). Getting up inside a big vehicle puts you on the floor beside the seat, if there's room.
- **Interior parts** (Interior tab): **Doors** (2×4, slide open for anyone walking up, shut behind them, watertight when shut), **Stairs** (a walkable flight up one cell), **Ladders** (push toward one to climb, back to go down, Space to let go), **Lamps** (the 18 nearest the camera light up). **Glazing** panes are now see-through.
- Rooms need about 4 cells (2 m) of headroom and doorways 2 cells wide to walk through. The **command post** by the depot is now a walk-in building with all of these.
- **Fire.** Fuel, engines, generators, ammunition, propellant, missile motors and electronics can catch fire when hit and not destroyed. A fire burns its block down, spreads to flammable neighbors, and burns out after about 30 s (18 s with crew aboard fighting it). Water inside the hull puts it out, and so does the welding torch played on it. A block that burns away goes up as if it were shot: fuel bursts and ammunition cooks off. The HUD warns while anything's burning.
- **Flooding you can see.** Each flooding compartment shows its water surface, rising as it fills.
- **Hidden machinery.** Blocks walled in on every side by solid plate aren't drawn (they reappear as soon as a neighbor is shot away).
- **Every fitting works** (nothing is just for looks):
  - **Periscopes** spot 15% further each (up to two). **Antennas** carry a console's orders 1 km further each (up to three).
  - **Toolboxes and stowage racks** let the crew patch damaged blocks once nothing has hit the vehicle for 8 s (3 HP/s each, up to three). **Spare track links** let them refit a lost track or wheel every 20 s (10 s with two sets). Neither rebuilds anything else: that's still the welder's job.
  - **Vents** make fires burn out 15% sooner each (up to three).
  - **Smoke launchers** (Z) throw a screen 22 m ahead that nobody can see through for 40 s; AI crews fire theirs when they've been hit hard. Reloaded at supply points.
  - **Headlights** light the way and the **searchlight** follows the aim (H toggles both). **Exhausts** smoke under load.
  - **Hatches** are ways in and out: E at a hatch takes the seat nearest it, and you get out through it. **Grab handles** climb like ladders. **Railings** stop people walking off an edge.
- **The Trident** is walkable inside: in at the bridge door (the wheel, console and commander's seat are at the front window), down the ladder through the bridge floor, and through a watertight door into the reactor room.

### Buildings
- **A design with a Foundation on its bottom row is a building.** It needs no driver, engine or wheels, stays exactly where it's put (anchored, nothing shoves it), and otherwise works like any vehicle: crew, guns, power, fire, fittings, interiors, the tactical map (it takes attack and rules-of-engagement orders, ignores move orders). Its guns need a Gunner Seat. Pieces shot loose from the ground fall away. **Concrete** (Structure, with all the shapes) is the heavy, cheap, blast-resistant wall material.
- **Interior items that work** (on buildings and vehicles alike):
  - **Bunk:** E to rest. You're healed, and when you fall you come back at your bunk instead of the depot, while it stands.
  - **First Aid Station:** heals anyone standing near it (15 HP/s).
  - **Weapon Locker:** restocks your rifle and rockets.
  - **Workbench:** a repair bay. Your vehicles parked within 14 m are patched up and have shot-away blocks rebuilt, one every 3 s. On a truck, a repair truck.
  - **Supply Crates:** a supply point. Your vehicles parked within 14 m are rearmed and refuelled, and people restock. On a truck, a supply truck. They burn and blow up if hit.
- **Why build a fort: heavy weapons and a steady mount.**
  - Every gun on a building spreads 40% less.
  - **Heavy weapons** fire at full rate from a building. A land vehicle can carry one but can only fire it stopped with two or more **Stabilizers** braced against the ground (they swing down when it stops), and any vehicle reloads it at half the rate and spreads it twice as much. Ships count as braced (still half rate); aircraft can't fire them at all.
  - The heavy weapons: the **420 mm Siege Mortar** (3×3×3, high-arc only, a 13 m blast, kilometers of range, from HE magazines), the **Heavy Railgun** (3×3×3, 120 mm slugs at about 3.4 km/s through 700 mm of armor, 150 MJ a shot), and the **Laser Battery** (3×3×2, point defense and anything else out to three or four kilometers, 25 MW while firing).
  - **The two big building weapons are for the sky and for lobbing:**
    - **128 mm Heavy Flak** (anti-aircraft only): it fires nothing but flak, and its crew never shoot it at the ground; with no aircraft up they keep it raised, watching the sky. Its crews pick out aircraft 7 km away; with eight Heavy Barrels its shells leave at about 820 m/s, burst within 6–7 m of an aircraft (bigger planes set them off further out) and fill 14 m of sky. 88° elevation, a shot every 3.5 s, fed from the new **Flak Magazine** (2×2×2, 192 heavy flak shells). Vehicles can approach a flak site safely: only its light guns fight them.
    - **160 mm Heavy Mortar** (one cell, two Heavy Barrels): lobs a 40 kg bomb at 45–85°, out to about 5–6 km, every 6 s, with a 6 m blast. Its crew picks the charge: full charge for far targets, fewer bags for close ones (down to a sixth), so the bomb still comes down steeply at about 70° instead of sailing over. Crews look out to 4.5 km. (The siege mortar picks its charge the same way.)
- **Examples:** the **Pillbox** (a 37 mm on a turntable firing through a slit, bunk, locker, first aid), the **Flak Nest** (a 40 mm inside a low wall, radar and searchlight), and the **Outpost** (a forward base: bunks, locker, first aid, a workbench, supply crates on its apron, stairs to an AA gun on the roof), and two fortresses for the heavy weapons:
  - **Siege Fortress**: a two-storey concrete bastion 12 m square on a sloped glacis, battlements round its terrace. The 420 mm mortar stands on the top roof behind a parapet, fed by a hoist (an ammo feed column) from the magazine hall on the ground floor. A flak gun on each terrace corner. Crew quarters, lockers, first aid and a workbench on the ground floor; stairs up to the command room with the console and windows all round; a ladder from the terrace to the mortar.
  - **Coastal Citadel**: a 17 m sea fort. Two heavy railguns in armored turret houses (they turn with the guns) face the sea; behind them a 16 m tower with a ladder inside carries the laser battery on a railed platform. A close-in gatling on each corner and twelve missile cells in the deck. Below decks: two fusion reactors, a gallery of twelve capacitors, the command bunker, quarters, a workbench and supply crates, and stairs up to the deck.

  - **Flak Tower**: a three-storey concrete keep, 12.5 m square and 10 m high, built to close the sky. A twin 128 mm heavy flak turret on each corner of the roof, on its own raised platform, with side shields and a flak magazine; each is fed by a hoist (a feed column through the roof) from another magazine on the top floor. A gallery on the front and back of the top floor carries two 40 mm flak guns each, behind railings. Inside: shelters with bunks, lockers and first aid on the ground floor; supply crates, a workbench and a locker on the first; the command room with the console and windows all round on the top; stairs between the floors and a ladder onto the roof; radar, searchlights and an antenna between the turrets.
  - **Mortar Battery**: four 160 mm mortars on turntables in a walled pit, each in its own bay between revetments, with ready racks beside it. An ammunition line under the floor runs from every mortar back to the magazine bunker (four HE magazines). Either side of the bunker, the crew dugout: bunks, lockers, first aid, a workbench, supply crates and the fire-direction console, with a periscope, radar and antenna on the roof. A footpath into the pit on each side.

  The **Juggernaut** is the vehicle answer: a tracked siege mortar carrier with four stabilizers. On the range, your outpost stands east of the depot with the siege fortress behind it; east of the lane, on ground levelled out of the hills, stand the flak tower, the mortar battery and the coastal citadel above the beach. An enemy pillbox and flak nest wait down range.

### Ground you can change
The first step of terrain and industry: the island's ground is no longer fixed.
- **Craters.** Any blast near the ground (not flak) digs one: a bowl with half its earth heaped in a rim round it; the rest is thrown far. Size follows the blast and how high above the ground it went off: a 76 mm shell scoops a hollow about 3 m across and 40 cm deep, a 160 mm mortar bomb a pit 9 m across and 1.5 m deep, a bomb one 10 m across and 2 m deep, the 420 mm siege mortar a hole 20 m across and 4 m deep. Nothing digs more than 8 m below the ground as it was (bedrock).
- **Earth slumps.** It can't stand steeper than about 48°: after every change, any steeper step slides until it can. Earth is never made or lost by digging, piling or slumping (craters throw half theirs away).
- **What it changes.** Shells, wheels, tracks and gunners' sight lines all use the changed ground at once: a tank in a crater or behind a berm is hull-down, one driving into a crater drops in, a parked vehicle wakes and settles when the ground goes from under it. Anything dug below sea level fills with water. Freshly turned earth is drawn as dark raw soil, on the tactical map too.
- **Buildings hold their ground.** The ground under buildings and range structures is locked: craters stop at their walls.
- **The shovel** (4, on foot): left click digs where you look, up to 3.6 m away, and the earth goes on your shovel (2 m³); dig on with it full and it's thrown ahead of the hole as a parapet. Right click piles what you carry where you look. About 0.5 m³ a second: a foxhole in about ten seconds, a pit for a tank in a minute or two.
- **The dozer blade** (Mobility, 3×2×1): mounted across the front with its bottom row level with the tracks or wheels. K (driver) lowers it: driving forward, it cuts 25 cm below the tracks and heaps the earth just ahead, where the blade cuts it again as it comes on, so a heap builds in front and is pushed along. Every pass goes deeper as the vehicle drives down into its cut: ramps, trenches, firing pits. Raised, it rides 30 cm above the tracks and only shifts earth that's higher than that. Pushing earth drags: 12 kN for every m³ a second it moves, up to 0.4 g, so a big heap slows you to a crawl. The blade takes everything above its edge in the strip it sweeps, the heap it is pushing too, so the heap goes on ahead whole, and what spills off its ends makes berms along the cut. It doesn't collide with the ground (it cuts it), but shells hit it like 35 mm of armor. The builder warns if a blade is too high to reach the ground.
- **The Mole** (example): an armoured dozer, a tracked hull with three blades 4.5 m across (wider than its tracks, so it can drive down into its own cut), a 37 mm turret, toolboxes, spare links and a searchlight. One waits by the command post.
- **How it's drawn.** The island is 8 × 8 tiles of 64 m, each its own mesh and height map: a change redoes only the tiles it touches, their collision at once and their meshes a few a frame.
- Next: resources in the ground, then machines that mine them.

### Mobility
- Wheels and track segments both use raycast suspension. Wheels steer; tracks skid-steer.
- Engine power and vehicle mass set acceleration and top speed.
- Fuel tanks feed the engines. An engine with no fuel connected gets a warning.
- The builder works out what a design is from its parts: wheels or tracks make it a **land vehicle**, ship propellers make it a **ship**, and ballast valves make it a **submarine**.

### Sea
- **Ships float on sealed air.** Armor and machinery only float by their own steel, so what keeps a ship up is the empty space its watertight hull encloses. The builder finds every sealed space (a **compartment**) by flooding the grid from outside. Frames, feeds, propellers and all the machinery inside let water past; armor, the turret ring, ballast valves and torpedo tubes don't.
- The builder shows the **waterline**, draft and freeboard, how many compartments the hull has, and whether it stays up with any one of them flooded.
- **Flooding:** a destroyed hull block below the waterline is a hole. Water comes in faster through more and deeper holes, and never rises higher than the sea outside. The ship settles, trims and lists as compartments fill. If enough of them flood, it sinks. **Bilge pumps** clear water, but can't beat an open hole: weld it shut first. Engines stop when the water inside reaches them.
- **Ship propellers** push along their arrow, but only under water. **Rudders** steer, and work better with speed and propeller wash; turned on their side they're **dive planes**. Ships keep the speed set on an **engine telegraph** (full astern to full ahead), so the player can leave the wheel and man a gun.
- **Submarines:** a compartment touching a **ballast valve** is a ballast tank. The player orders a depth and the boat floods and blows its own tanks and works the planes to hold it. A design that can't dive with its tanks full, or can't surface with them blown, gets a warning.
- **Torpedoes** (fired from tubes and fed from torpedo racks, from any seat) drop in, settle just under the surface, turn onto the bearing the player aimed at, and run straight. They go off against a hull or passing just under the keel, and blow holes below the waterline. Shells that hit the sea splash; HE goes off on the surface; AP shells dive a few dozen meters, slowing fast.
- Land vehicles that drive into deep water mostly sink (their blocks hold up about a third of their weight) and stall once the engine is under. People swim.

### Air
- The builder works out what an aircraft is from its parts: wings and propellers or jets make a **plane**, a rotor makes a **helicopter**, lift fans or upward jets make a **VTOL or drone**, and enough gas cells make an **airship**.
- **Every wing panel and control surface lifts and drags where it sits**, depending on its angle to the airflow, and stalls past about 17°. A plane's stability comes from its shape: wings that lift just behind the center of mass are stable, too far behind is nose-heavy, and ahead of it flips. The builder shows stall speed and top speed, and warns about each of these, about missing control surfaces, and about landing gear the plane would tip off.
- **Control surfaces map themselves** from where they are: at the wingtips they roll (ailerons), on the tailplane they pitch (elevators), standing up on the fin they turn (rudders). How hard a plane can turn depends on their size, how far out they sit, and the airspeed.
- **Forgiving pilot assists:**
  - Planes fly toward where the mouse points. The assist banks into turns, only as steeply as the wings can hold the weight, levels the wings on target, and won't pull into a stall. W / S set the throttle, and A / D roll by hand.
  - Helicopters and drones move at the speed you ask for, stop and hover when you let go, hold their height, and turn to face where you look.
  - Airships hold the height you order by venting and topping up gas, and use the telegraph like ships.
- **Engines share their power** between everything they turn: air propellers (strong when slow, fading with speed), rotors and lift fans (momentum theory). **Jets** push on their own, at any speed, and only need fuel. **Gas cells** lift 20 kg each. That's far more than real gas at this size, so airships stay buildable.
- **Drones** are flown by remote from a **Drone Core**: the player stays standing where they are (and can be shot), and losing the drone costs no one.
- **Weapons:**
  - **Bombs** (250 kg) hang in bomb racks and fall with the plane's speed; a bombsight diamond shows where they'll land.
  - **Missiles are built from parts** lined up on a rail: rocket motors (push and burn time), warheads (blast), fins (how hard it turns) and a seeker on the nose (optical flies to the crosshair; radar locks on to the enemy aircraft nearest it and chases it). The parts leave the vehicle when fired, and supply points build new ones.
  - The **40 mm autocannon** fires three rounds a second and elevates to 85°. **Flak** shells burst near aircraft, or at the range the gun is aimed at, in a puff of black smoke. Q cycles AP, HE and Flak.
- The test range has an **airstrip** cut through the western hills and a **helipad**, both of which rearm aircraft. A target plane flies circuits round the island, a helicopter hovers over the eastern hills, and an airship floats off the north-east coast.

### Machines of scale
- **Big blocks** fill a box of cells and count as one block: one health pool, placed, mirrored, destroyed and welded back whole. They let a 300 m hull be built from hundreds of pieces instead of hundreds of thousands. The set so far:
  - Hull plate (4×1×4), heavy armor block (2×2×2) and belt armor (4×4×2, 340 mm).
  - Marine turbine (2×2×3, 6 MW), fuel bunker, heavy track, and the great screw and great rudder for big ships.
  - 203 mm and 406 mm breeches with heavy barrels, AP and HE shell magazines.
  - Ballast block (11 t of lead) and gas envelope (lifts 1.3 t).
- A big block turns with the rotate keys like any other. The ghost shows its whole box, and it only fits if every cell in the box is free.
- **The Colossus** is the example: a 300 m battleship of 51,000 t, with a watertight bulkhead every 20 m, twelve turbines on four great screws, a bridge tower, and two twin 406 mm turrets. It's too long for the buoy ring, so ships over 80 m start (and resupply) at a **fleet anchorage** further out to sea.
- Big ships steer the same way small ones do, but the rudder pushes through the water's resistance to turning, so a 300 m ship still comes round in a few minutes.
- **Big machines bristle with weapons.** The Colossus carries twelve 406 mm guns in four triple turrets (the inner two raised on barbettes to fire over the outer ones), twenty-four 203 mm guns in twelve twin turrets down her sides, forty-eight 40 mm flak guns in twenty-four mounts, eight torpedo tubes at the waterline and eight radar missiles. Her bow tapers to a point, and her bridge is stepped. The Zephyr airship carries a flak turret on top of its envelope, four guided missiles and eight bombs.
- **Rules that make a lot of guns workable:**
  - Every turret has its own gunner. With gunners following the player's aim, one click fires every gun that has come round onto the target. Guns still turning hold fire.
  - A gun never fires into its own ship: if the hull, a turret or the superstructure is in its line of fire, it holds fire (the HUD says how many guns bear).
  - Anti-aircraft guns keep their own watch. While enemy aircraft are up, guns with flak engage them (out to 2.5 km) and the rest stay on ships and vehicles.
  - F fires torpedoes on anything with tubes; X fires missiles.
  - The HUD shows one line per kind of gun. A broadside's sound, muzzle effects and camera kick are capped so it stays readable.

### Big battles (performance)
The goal is hundreds of vehicles and thousands of shells in the air at once, on an ordinary gaming PC. What makes that possible:
- **Shells are plain data.** Each shell is a few numbers in a list, not a node. Every tracer in the world is drawn in two batches (streaks and trails, and the glowing heads), refilled each physics step. Far away, tracers are drawn slightly thicker than they really are so they stay visible.
- **Shells don't ask the physics engine where they are.** Calling into the engine from C# costs several microseconds, and a battle would make tens of thousands of calls a second. Instead:
  - The ground is the island's height grid, tested exactly in C# (`HeightField`).
  - Vehicles, structures and wrecks are filed in a flat grid of 48 m squares every step (`SphereGrid`), so a shell only looks at what's near its path. Those are then tested block by block, as before.
  - The engine is only asked near people and pop-up targets, or near solid scenery that isn't the ground (ramps, crates).
- **Wheels** on open ground test the height grid too; near other vehicles or scenery they use the engine as before.
- **Parking.** An AI ground vehicle that has sat still with its brakes on for a moment (and is well away from the camera) is frozen in place. It costs the physics engine nothing until it's hit by a blast, damaged, given an order, or the camera comes close.
- **Gunners think less often far away.** Within 500 m of the camera they aim and decide every physics step; out to 1.5 km every other step; beyond that every fourth (in bigger steps, so they turn just as fast). Target picking only looks at vehicles in reach (through the grid), nearest first, and stops after a few line-of-sight checks. Gunners spread their target checks out so a hundred vehicles don't all look round on the same step. Big naval guns (150 mm and up) look for targets out to 3 km.
- **Shared looks.** Vehicles built from the same design share their meshes. A vehicle gets meshes of its own only for parts of it that get damaged.
- **Far models.** Each hull, turret and gun also has a one-piece low-detail model: plating as flat panels, other parts in simple shapes, in their own colors. Past a distance that grows with the vehicle's size (about 160 m for a tank; things over 8 m across switch sooner for their size, about 300 m for a fort), the detailed batches switch off and the far model shows instead, so a distant tank costs about 3 draw calls instead of dozens. Looking through a zoomed sight stretches that distance, so what you're zoomed in on keeps its detail. Far models are rebuilt after damage, at most every few seconds and only while they're being looked at.
- **Far models by the chunk.** A section more than 16 m long (a big ship's hull, a long wall) gets a far model for each 8 m chunk instead of one for the whole, and each chunk switches at about 280 m on its own. The far end of a 300 m battleship is drawn simply while the end you're on stays detailed, and damage only redoes the far model of the chunk it hit (the whole hull used to be redone, a stall on the biggest ships).
- **Detail by size.** Small parts (under 14 cm: bolts, rails, handles) and small fittings (hatches, periscopes, ladders, stairs, seats, loaders, feeds, crates) are drawn out to 90 m; interior items (bunks, lockers, first aid, workbenches, lamps, consoles) out to 45 m. Neither casts a shadow, and neither is in the far models. In the Flak Tower that leaves about a third of its batches casting shadows, and past 90 m drawing a third of them.
- **Cheaper sun shadows.** The sun's shadows use two splits instead of four, out to 200 m: everything that casts a shadow is drawn once per split.
- **Effect budgets.** Every effect asks how much detail it's worth where it is: the full effect close up, a flash and a little smoke further out, only the sound beyond that. A crowded scene shrinks those distances. Only 24 flashes of light can be lit at once, and only near the camera. Effects reuse their meshes, materials and curves instead of making new ones each time.
- **Measuring.** F3 shows frame rate, draw calls, counts (vehicles, far models, parked, shells, tracer pieces, effects, lights) and how many milliseconds go to shells, explosions, driving, AI gunners, ships, aircraft, effects and mesh rebuilds. F8 on the range starts a **battle drill**: about 100 vehicles in two armies (tanks across the firing lane, patrol boats at sea, planes and helicopters overhead). F8 again starts the big drill, about 200 with a battleship on each side, and once more clears it.
- **Benchmark** (headless, the core parts only): 200 tanks and 3,000 shells in flight take about 3.4 ms a step to check against the vehicles (7.2 ms testing every vehicle for every shell, plus 600,000 engine calls a step that the old way also made to read vehicle positions) and 4 ms against the ground in hilly country. 1,400 wheel rays on open ground take 0.45 ms.
- **The driving camera** starts at a distance that suits the machine (about 13 m for a tank, about 200 m for a 300 m battleship). The wheel brings it in as close as you like, but it always stays outside the machine's own box, whichever way you look (close beside a long ship, past its stern from behind). Over water it never dips under the waves: looking up lifts the point it circles instead. A machine's size for this, for the map and for hit checks counts how far its turrets can actually swing, not their distance from the middle twice over (that made a battleship's size about 425 m instead of 153).
- **Haze** is thickest at sea level and thins as the camera climbs (at 300 m it's about a third), so from the air the island stays clear below you while the horizon stays soft. Below the horizon the sky is the far sea in haze, and the sea reaches 12 km, so there's no grey band at the edge of the world from up high.
- **Baked chunks.** All the parts in a 16-cell chunk that share a material and a detail tier are copied into one mesh, already in place: one draw call per material (and one per shadow split) instead of one per kind of part. Normals turn with the parts, and the chip marks come along. Machines of the same design share the baked meshes. Hits redo only the chunk they land in.
- **Water only where there's water.** A vehicle works out buoyancy, drag and flooding only when the lowest corner of its box is within half a metre of the sea. Every tank on the island used to do the full per-brick sums every physics step; on the owner's PC that was most of the battle drill's frame.
- **No catching-up spiral.** When a frame takes too long, the physics runs at most 3 steps to catch up (it was 8). Below about 20 FPS the battle slows down a little instead of every frame doing eight steps' work and the frame rate collapsing (the drill sat at exactly 7.5 FPS, 60 ÷ 8).
- **Optimized C# in every build.** The project asks for optimized code even in the Debug build the editor runs, so the JIT optimizes the game's hot loops.
- **Benchmark runs.** Started from the command line (`godot --path . --maximized -- --bench=deck --seconds=45`), the game skips the builder, sets up a scene on the range and plays it by itself: **idle** (the Warden at the depot), **deck** (the Colossus at the anchorage, live fire from 5 s), **jet** (the Falcon takes off and circles at about 400 m), **drill** and **bigdrill**. Each second it writes the frame rate, the renderer's counts and the milliseconds per frame in each part of the game to `bench.csv`, takes screenshots (start, middle, end, and the slowest moments), writes `summary.txt` and quits. `--out=<folder>` says where (otherwise the game's user folder, under `bench`); `--fullres` draws every pixel.
- **Big screens.** The HUD was laid out for about 1080 lines; on a taller screen it's scaled up to match (1.75× at 4K). The 3D world is drawn at about 1150 lines and sharpened up to the screen (FSR 1). A 4K window has about four times the pixels to light, fog and shade, and on most cards that costs more frame rate than the extra sharpness is worth. F4 switches to drawing every pixel and back; F3 says which is on.
- **Not yet:** simplified far-off battles with no physics at all, and running the shell checks on several CPU cores. Both are planned once fleet command makes battles bigger than the island.

## Technical approach

- **Engine:** Godot 4.7 with C# and Jolt physics.
- **The core rules don't depend on Godot.** Block definitions, blueprints, vehicle stats, gun stats, penetration, explosions and connectivity live in `src/Core` as plain C#. They're unit tested with xUnit, and they're what will make multiplayer and simplified far-off battles possible.
- **Multiplayer-ready from day one.** Game logic runs as if a server is in charge, even in solo play, so co-op can arrive early instead of being bolted on.
- **Scale:** a block is 0.5 m. Vehicles stay one physics body until pieces break off. To keep colossal machines cheap:
  - Blocks are drawn in chunks of 16×16×16 cells, one batch per part and material, and only a chunk that changed is redrawn. Blocks that move (turrets, propellers, rotors, control surfaces) keep their own nodes.
  - Collision uses big boxes merged from the solid blocks, rebuilt only in the chunk that changed.
  - Buoyancy is worked out per "brick" of cells (sized so even a 300 m ship has a few thousand of them), instead of per block.
  - Flooding finds which compartments a hole opens to by joining destroyed hull blocks, instead of re-flooding the whole grid.
  - The builder analyzes big designs in the background, so placing blocks on a huge design stays smooth.
- **Looks come from the core.** Each block's model is described in `src/Core/BlockModels.cs` as a list of simple shapes (boxes, cylinders, rings, tapered slabs), and hull surfaces are built by `src/Core/HullMesher.cs`. The game turns both into meshes, and so do the preview pictures, so what's drawn outside the engine matches the game's models exactly.
- **Damping:** vehicles replace Godot's default damping (which pulls in proportion to mass) instead of adding to it. Ships and aircraft model their own drag and get almost none; land vehicles keep the amount they've always had.
- **Water** is a flat surface for physics (the waves are only drawn). Every block and every compartment cell under it pushes up on the hull where it is, so trim, list and capsizing come out of the shape. Water drag on each piece gives a ship its keel and its roll damping. Submarine depth keeping and rudder steering have gentle assists, and a standalone simulation checks the example ships float level, right themselves, turn and dive before each change ships.
- **Flight** uses the same approach: every panel's lift and drag is applied where it is, and the assists are rate controllers on each axis, capped by what the control surfaces can do at the current airspeed. A standalone flight simulation checks the examples take off within the airstrip, hold level flight, turn, recover from a zoom climb, and (for helicopters, drones and airships) hover and hold height.
- **Level of detail:** full block simulation near players, with gunners thinking less often, simpler far models and lighter effects further out (see *Big battles*). Far-off battles beyond the map will use the core stat math with no physics.

## Roadmap

Each milestone is playable on its own.

1. **Builder + test range** *(built; polish pass done after the first playtest)*
   - Block-by-block builder: about 25 blocks, mirror mode, live stats and warnings, save and load.
   - Guns built from components; turrets.
   - Drive with real mass and recoil.
   - Block-by-block damage and break-off debris.
   - A test range with target structures, range markers and hit feedback (penetration vs. armor).
   - Example designs.
2. **On foot + crew** *(built; being playtested)*
   - First-person on foot: move, sprint, jump, rifle, AT rocket, welding torch.
   - Gunner and commander seats; get in and out and switch seats; AI crew that follows your aim or fights on its own.
   - Field repair and rebuild by hand; supply pad; pop-up targets; live fire (the range's parked vehicles shoot back).
   - Not yet: the first co-op test. Controls already go through plain data (`VehicleControls`), so remote players can plug into the same path. That's next, along with enemy soldiers on foot.
3. **Sea, air and machines of scale** *(built; being playtested)*
   - **Sea** *(built)*: ships and submarines, buoyancy from sealed compartments, flooding and bilge pumps, propellers, rudders, dive planes, ballast and depth keeping, torpedoes, the engine telegraph, swimming. The test range is now an island with open sea off the east coast, an enemy patrol boat and a surfaced submarine.
   - **Air** *(built)*: wing panels and self-mapping control surfaces, air propellers, aero engines, jets, rotors, lift fans and gas cells; planes (mouse aim), helicopters, VTOL and drones (flown by remote), airships; bombs with a bombsight, guided missiles built from parts (optical or radar), a 40 mm autocannon with air-burst flak; an airstrip, a helipad, and enemy aircraft on the range.
   - **Scale** *(built)*: colossal machines, 300 m and up. Big multi-cell blocks, chunked drawing and merged collision, bricked buoyancy, a 320 m build area, box fill / hollow / clear tools, background analysis, the Colossus battleship and the fleet anchorage.
   - **Looks and firepower** *(built)*: merged hull surfaces with chamfers, shading and weathering; detailed block models; the Colossus armed with 84 guns, torpedoes and missiles; firing arcs, the anti-aircraft watch and a grouped weapons HUD.
   - **Fleet command** *(built)*: the command console and search radar, the command post, the tactical map with fog of war, picking and groups, move / attack / escort / patrol / hold orders in formation, rules of engagement, command range, jumping into units, and deploying more.
   - **Looks and building** *(built)*: the shape kit, paint jobs with ship rules, glazing and the pilot seat, detail parts, joints, copy and paste with saved parts, the armor view, and the examples rebuilt with them.
   - **Big battles** *(built)*: shells as data, two-batch tracers, ground and vehicle checks in C#, parking, far models, shared meshes, effect budgets, the F3 performance readout and the F8 battle drill.
   - **Buildings** *(built)*: foundations and concrete, stationary crewed units, bunks, first aid, weapon lockers, workbenches (repair bays), supply crates (forward supply points), the pillbox, flak nest and outpost.
   - **Interiors** *(built)*: walking on moving decks, taking the seat you walk up to, doors, stairs, ladders, lamps, see-through glass, fire that spreads and can be put out, visible flooding, hidden enclosed machinery, the walk-in command post.
   - **Modern and future** *(built)*: jets with afterburners and the Falcon; power (generators, the fusion reactor, capacitors); railguns, lasers, the CIWS and point defense; vertical launch cells with strike and air-defense missiles; rocket artillery; big blasts with shock waves; the Vanguard, Hailstorm and Trident examples.
   - Not yet: giant aircraft and land battleships. They can be built from the same blocks, but their wings and tracks are still worked out panel by panel, so a giant one costs more to fly than it needs to.
4. **Terrain + industry:** deformable terrain, mining, machines you design, conveyors, power, assembly bays that print blueprints.
   - **Ground you can change** *(built)*: craters from blasts, slumping earth, the shovel, the dozer blade and the Mole, locked ground under buildings, flooding below sea level, tiled ground updates.
   - Next: resources in the ground (surveying, seams), mining machines, moving materials, factories, assembly bays.
5. **Planet war:** a generated planet, a rival faction that designs its own vehicles, territory, the supply network, research, salvage, captured sites, simplified far-off battles.
6. **Space:** the system map, capital ships, orbital strikes, landings, scouting, supply between planets.
7. **Co-op campaign, PvP, configurable factions, Steam (GodotSteam, Workshop).**

## Risks

- **Scope.** This is a multi-year vision. We protect the pillars and cut everything else first.
- **Art.** Blocks, terrain and lighting can look good. Characters and animation start from free art packs and get replaced later.
- **Networking.** Keeping block damage in sync between players is the hardest technical problem. Game logic runs as if a server is in charge, and the rules are deterministic.
- **Colossal machines.** A 300 m machine is 600 cells long and could hold hundreds of thousands of blocks. Big blocks, chunked drawing, merged colliders and bricked buoyancy keep the Colossus cheap. Still to prove on a real PC: frame rate with several of them in a fight, the cost of heavy shell fire breaking pieces off a giant hull, and a full broadside plus forty-eight flak guns firing at once.
- **Testing.** Claude builds in a cloud container with no screen. Automated tests cover the core rules, and in-game feel needs playtesting on a real PC.
