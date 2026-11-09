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

### Mobility
- Wheels and track segments both use raycast suspension. Wheels steer; tracks skid-steer.
- Engine power and vehicle mass set acceleration and top speed.
- Fuel tanks feed the engines. An engine with no fuel connected gets a warning.

## Technical approach

- **Engine:** Godot 4.7 with C# and Jolt physics.
- **The core rules don't depend on Godot.** Block definitions, blueprints, vehicle stats, gun stats, penetration, explosions and connectivity live in `src/Core` as plain C#. They're unit tested with xUnit, and they're what will make multiplayer and simplified far-off battles possible.
- **Multiplayer-ready from day one.** Game logic runs as if a server is in charge, even in solo play, so co-op can arrive early instead of being bolted on.
- **Scale:** a block is 0.5 m. Vehicles stay one physics body until pieces break off.
- **Level of detail:** full block simulation near players. Far-off battles use the core stat math with no physics.

## Roadmap

Each milestone is playable on its own.

1. **Builder + test range** *(current)*
   - Block-by-block builder: about 25 blocks, mirror mode, live stats and warnings, save and load.
   - Guns built from components; turrets.
   - Drive with real mass and recoil.
   - Block-by-block damage and break-off debris.
   - A test range with target structures, range markers and hit feedback (penetration vs. armor).
   - Example designs.
2. **On foot + crew:** walk around, enter and leave seats, AI crew, infantry weapons, welding and repair by hand. First co-op test.
3. **Terrain + industry:** deformable terrain, mining, machines you design, conveyors, power, assembly bays that print blueprints.
4. **Planet war:** a generated planet, a rival faction that designs its own vehicles, territory, the supply network, research, salvage, captured sites, simplified far-off battles.
5. **Space:** the system map, capital ships, orbital strikes, landings, scouting, supply between planets.
6. **Co-op campaign, PvP, configurable factions, Steam (GodotSteam, Workshop).**

## Risks

- **Scope.** This is a multi-year vision. We protect the pillars and cut everything else first.
- **Art.** Blocks, terrain and lighting can look good. Characters and animation start from free art packs and get replaced later.
- **Networking.** Keeping block damage in sync between players is the hardest technical problem. Game logic runs as if a server is in charge, and the rules are deterministic.
- **Testing.** Claude builds in a cloud container with no screen. Automated tests cover the core rules, and in-game feel needs playtesting on a real PC.
