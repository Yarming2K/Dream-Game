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
- What's next for command: zones and objectives to fight over, enemy commanders that give their own orders, and supply.

### Style rules
What every part should follow, so a design reads as one machine however odd it is:
1. **Flat faces, chamfered edges.** Curves only where something turns or holds pressure: wheels, barrels, tanks, rotors, round hulls.
2. **Glass is flat panes in frames,** never a bubble.
3. **Detail everywhere, at about the same density.** Plating gets panel seams and grime; parts get bolts, rims, hinges and handles; nothing large is featureless.
4. **Parts show how they attach:** axles, collars, mounts, shafts.
5. **Muted, real-world colors** for structure; bright colors only for lamps, markings and paint jobs.
6. **People-sized cues** (hatches, ladders, railings, handles) so the scale reads.

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
- **Far models.** Each hull, turret and gun also has a one-piece low-detail model: plating as flat panels, other parts in simple shapes, in their own colors. Past a distance that grows with the vehicle's size (about 180 m for a tank, 1.6 km for the Colossus), the detailed batches switch off and the far model shows instead, so a distant tank costs about 3 draw calls instead of dozens. Looking through a zoomed sight stretches that distance, so what you're zoomed in on keeps its detail. Far models are rebuilt after damage, at most every few seconds and only while they're being looked at.
- **Effect budgets.** Every effect asks how much detail it's worth where it is: the full effect close up, a flash and a little smoke further out, only the sound beyond that. A crowded scene shrinks those distances. Only 24 flashes of light can be lit at once, and only near the camera. Effects reuse their meshes, materials and curves instead of making new ones each time.
- **Measuring.** F3 shows frame rate, draw calls, counts (vehicles, far models, parked, shells, tracer pieces, effects, lights) and how many milliseconds go to shells, explosions, driving, AI gunners, ships, aircraft, effects and mesh rebuilds. F8 on the range starts a **battle drill**: about 100 vehicles in two armies (tanks across the firing lane, patrol boats at sea, planes and helicopters overhead). F8 again starts the big drill, about 200 with a battleship on each side, and once more clears it.
- **Benchmark** (headless, the core parts only): 200 tanks and 3,000 shells in flight take about 3.4 ms a step to check against the vehicles (7.2 ms testing every vehicle for every shell, plus 600,000 engine calls a step that the old way also made to read vehicle positions) and 4 ms against the ground in hilly country. 1,400 wheel rays on open ground take 0.45 ms.
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
   - Not yet: giant aircraft and land battleships. They can be built from the same blocks, but their wings and tracks are still worked out panel by panel, so a giant one costs more to fly than it needs to.
4. **Terrain + industry:** deformable terrain, mining, machines you design, conveyors, power, assembly bays that print blueprints.
5. **Planet war:** a generated planet, a rival faction that designs its own vehicles, territory, the supply network, research, salvage, captured sites, simplified far-off battles.
6. **Space:** the system map, capital ships, orbital strikes, landings, scouting, supply between planets.
7. **Co-op campaign, PvP, configurable factions, Steam (GodotSteam, Workshop).**

## Risks

- **Scope.** This is a multi-year vision. We protect the pillars and cut everything else first.
- **Art.** Blocks, terrain and lighting can look good. Characters and animation start from free art packs and get replaced later.
- **Networking.** Keeping block damage in sync between players is the hardest technical problem. Game logic runs as if a server is in charge, and the rules are deterministic.
- **Colossal machines.** A 300 m machine is 600 cells long and could hold hundreds of thousands of blocks. Big blocks, chunked drawing, merged colliders and bricked buoyancy keep the Colossus cheap. Still to prove on a real PC: frame rate with several of them in a fight, the cost of heavy shell fire breaking pieces off a giant hull, and a full broadside plus forty-eight flak guns firing at once.
- **Testing.** Claude builds in a cloud container with no screen. Automated tests cover the core rules, and in-game feel needs playtesting on a real PC.
