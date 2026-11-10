# Dream Game

A military-industrial sandbox: design everything, then fight a war for a star system. Working title.

The scope we agreed on is in [docs/DESIGN.md](docs/DESIGN.md). Built so far: **milestone 1** (the builder and test range), **milestone 2** (on foot, crew and field repair) and **milestone 3**: the sea and air fronts (ships, submarines and torpedoes; planes, helicopters, drones and airships, with bombs, missiles and flak) and machines of scale (big blocks, box tools and a 300 m battleship carrying 84 guns). Designs now draw as continuous armor plate with chamfered edges and weathering, and every part has a detailed model.

## Play it

1. Install **Godot 4.7.2, .NET edition** from [godotengine.org/download](https://godotengine.org/download). Get the one labeled ".NET"; the standard one can't run C#.
2. Install the **.NET 8 SDK** from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download/dotnet/8.0).
3. Get this repo (Code → Download ZIP, or clone it). Make sure you're on the branch you want to test.
4. Open Godot, click **Import**, and pick the `project.godot` file in this folder.
5. Press **F5** (or the ▶ button, top right). The first run takes a moment to build.

The game opens in the builder with an example tank loaded. Press **Tab** to drive it.

## Builder

| Do this | To |
|---|---|
| Left click (hold and drag to paint) | Place a block |
| Right click | Remove a block |
| Middle click | Pick up the block under the cursor (type and turn) |
| R / T / G (Shift reverses) | Rotate the block |
| V (Shift goes back) | Next shape, for armor, airframe and glazing: slope, shallow slope, corners, rounded edge, half block, plate, octagon |
| Ctrl+C, then drag a box | Copy part of the design |
| Ctrl+V, then click | Paste it. While pasting: R T G turn it, H flips it, Esc or right click stops |
| O | Armor view: blocks colored by armor thickness |
| M | Mirror mode (on at start) |
| B | Box tool: fill, hollow (walls, floor and roof), clear, off. Drag out the box; the wheel makes it taller while you drag |
| 1-9, [ and ] | Pick a block, switch category |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Right drag, middle drag, wheel | Orbit, pan, zoom |
| W A S D, Q / E | Move the camera around, down / up |
| F | Focus the camera |
| Ctrl+S | Save |
| Tab | Test drive |
| F1 | Help |

The top bar has the **paint job** (woodland, desert, dazzle, naval grey and more; ships always get red below the waterline and grey decks) and **Save part**, which keeps what you copied for later (Load, Parts). The **parts drawer** on the left shows every block with a thumbnail; the card under it lists the selected part's stats. The **dashboard** on the right updates as you build: mass, speed, armor bars (the tick marks the weakest spot), gun stats and a list of problems in plain English. The pink dot is the center of mass. Blocks tinted red aren't attached and would fall off.

### How designs work
- The front of a vehicle faces the **FRONT** marker.
- Every vehicle needs a **driver seat**, an **engine**, a **fuel tank**, and **wheels or tracks** on the bottom row.
- **A gun is a breech with barrel segments in a straight line in front of it**, on the side its yellow arrow points to.
  - **Loaders** (faster reload) and **propellant** (more velocity) only count if they touch the breech.
  - **Ammo racks** must touch the breech, or connect to it through ammo feeds, other racks or a turret ring.
- Everything you build on top of a **turret ring** (connected to the block directly above it) turns as a turret.

### Ships and submarines
- Put **Ship Propellers** (Sea category) on a design instead of wheels and it becomes a ship. A **Ballast Valve** makes it a submarine.
- **Ships float on the sealed air inside the hull**, not on their armor. Build a closed hull of armor, and leave the inside empty (machinery, seats and ammo can go in there). The blue plane in the builder is the waterline.
- **Inner walls make compartments.** One hole then floods one compartment instead of the whole ship. The dashboard tells you whether the ship stays up with any one compartment flooded.
- Propellers and rudders go **below the waterline**, usually at the stern. A rudder turned on its side (G) is a dive plane.
- **Submarines:** wall off the space low in the hull into tanks and give each one a ballast valve. The dashboard says whether it can dive and surface; add Lead Ballast if it can't dive.
- **Torpedo tubes** fire the way their arrow points and are fed by **Torpedo Racks** touching them (or through ammo feeds).
- Try the **Corsair** (patrol boat) and **Barracuda** (submarine) examples.

### Aircraft
- **Planes:** rows of **Wing Panels** (Air) lift with speed. Put **Control Surfaces** at the wingtips (they roll), on a tailplane behind the wings (they pitch) and standing up on a tail fin (they turn); the dashboard shows what each one does. Pull with an **Air Propeller** and an engine (the **Aero Engine** is light and strong), or push with **Jet Engines**. Give it **Wheels** with one under the nose, and keep the main wheels just behind the pink dot.
- **The wings should lift just behind the center of mass.** The dashboard warns if it's nose-heavy (it can't lift its nose to take off) or tail-heavy (it flips).
- **Helicopters:** a **Rotor** on top lifts it (one big engine lifts about three tonnes). Keep 4.5 m around the rotor clear.
- **Drones and VTOL:** four **Lift Fans** pointing up at the corners. A **Drone Core** instead of a seat means you fly it by remote. With wings and a propeller too, V switches between hovering and flying on the wings.
- **Airships:** fill an envelope with **Gas Cells** (each holds up 20 kg) and hang a gondola underneath.
- **Missiles** are built on a **Missile Rail**: line up a Rocket Motor, Fins, a Warhead and a seeker on the nose (Optical flies to your crosshair, Radar chases aircraft). More motors go faster and further, more warheads hit harder. **Bomb Racks** carry one bomb each. The **40 mm Autocannon** with a **Flak Rack** shoots down aircraft.
- **Jets:** open the throttle all the way (hold W) to light the afterburners: much more push, much more fuel.
- Examples: **Kestrel** (fighter-bomber), **Falcon** (jet fighter), **Hornet** (helicopter), **Wasp** (drone), **Zephyr** (airship) and **Skyguard** (flak truck).

### Modern and future weapons
- Every weapon is a breech with its own kind of barrel in front: **Railgun Breech + Rail Segments**, **Laser Emitter + Focusing Lenses**, **20 mm Gatling (CIWS) + barrels**, **Rocket Launcher + Launch Tubes**.
- Railguns and lasers need **power** (the Power tab): a **Generator** (needs fuel) or a **Fusion Reactor**, and **Capacitor Banks** to store charge. The dashboard tells you if there's enough.
- The **CIWS** and the **laser** shoot down incoming missiles and bombs on their own.
- **VLS cells** sit flush in a deck with their hatch on top: the red-striped ones hold strike missiles (they fly to the spot under your crosshair, kilometers away), the gold ones air-defense missiles (they lock on to aircraft). Fire with F.
- Examples: **Vanguard** (railgun tank), **Hailstorm** (rocket artillery) and **Trident** (missile destroyer).

### Big machines
- The **big blocks** (their names end in their size, like **Hull Plate (4×1×4)**) fill a box of cells and act as one block: one health bar, destroyed and welded back whole. Use them for anything large: hull plates and belt armor for a hull, marine turbines, fuel bunkers and great screws for machinery, 406 mm breeches and shell magazines for the guns, ballast blocks for weight down low.
- The ghost shows the whole box. It turns red if any cell in it is taken.
- **Box hollow** (B, twice) builds a hull's walls, floor and roof in one drag. **Box clear** cuts doorways and compartments back out.
- The build area is 320 m across and 80 m high.
- Big designs take a moment to analyze: the dashboard catches up a second or so after you place a block.
- **Armor merges into plate.** Blocks of the same armor next to each other draw as one continuous surface, with chamfers only on the outside edges. Slopes still smooth off corners.
- Try the **Colossus** (300 m battleship). Press F to frame it. She carries twelve 406 mm guns in four turrets, twenty-four 203 mm, forty-eight 40 mm flak guns, eight torpedo tubes and eight missiles. Look at how a turret is built: a ring, breeches and barrels, loaders, magazines, a gunner seat, and an armored box around them, with a gap between it and anything else on deck (whatever touches a turret above its ring turns with it).

Saved designs are `.json` files in your blueprints folder. The **Folder** button opens it, so you can send files to friends.

## Test range

You start in the driver's seat. Press **E** to get out and walk around; look at your vehicle and press **E** to get back in.

**In a vehicle**

| Do this | To |
|---|---|
| W / S, A / D, Space | Drive, steer, brake (tanks turn on the spot) |
| Mouse | Aim: turrets turn and guns elevate on their own |
| Left click / right click | Fire / zoom (the commander's periscope zooms much further) |
| Q | Switch between AP and HE |
| 1 / 2 / 3 | Move to the driver, gunner or commander seat |
| G | AI gunner follows your aim, or picks its own targets |
| R | Flip back upright |
| E | Get out |

**On foot**

| Do this | To |
|---|---|
| W A S D, Shift, Space | Move, sprint, jump |
| 1 / 2 / 3 | Rifle, AT rocket, welding torch |
| Left click / right click | Fire / aim |
| R | Reload |
| E | Get into the vehicle you're looking at |

The **welding torch** repairs damaged blocks and rebuilds destroyed ones. Missing blocks show as blue outlines: aim at one and hold the trigger. The **supply pad** next to the start (the ring on the ground) rearms and refuels vehicles parked on it, and heals and restocks you.

**At sea** (ships and submarines start off the island's east coast)

| Do this | To |
|---|---|
| W / S | Move the engine telegraph a notch: full astern … stop … full ahead. The ship holds that speed, even when you leave the wheel for a gun |
| A / D | Rudder |
| Space | All stop (submarines: hold to come up) |
| C | Submarines: hold to order the boat deeper. The crew floods and blows the tanks to hold the depth you order |
| F | Fire a torpedo (from any seat). It turns onto the bearing you're aiming at, then runs straight just under the surface |
| X | Fire a missile, if the ship carries any |
| Left click | Fire every gun that bears on your aim. Guns still turning, or with your own ship in the way, hold fire |
| R | Put the boat back upright |
| E | Get out onto the deck |

Holes below the waterline flood the hull: the HUD shows each compartment filling. Get out, weld the holes shut, and the bilge pumps clear the water. Stop inside the **buoy ring** to rearm and refuel. Ships over 80 m long start at the **fleet anchorage**, a wide ring of buoys further out, which rearms them too. The mouse wheel pulls the camera back far enough to see a whole battleship. In the water you swim: Space up, C down, and E climbs into a boat you're looking at.

**In the air** (planes start on the airstrip in the western hills; helicopters, drones and airships at the helipad)

| Do this | To |
|---|---|
| Mouse | Planes fly toward where you point. Helicopters and drones turn to face where you look |
| W / S | Planes: throttle up / down (it stays where you leave it). Helicopters and drones: forward / back. Airships: telegraph |
| A / D | Planes: roll (and steer on the ground). Helicopters and drones: sideways. Airships: turn |
| Space / C | Helicopters, drones and airships: up / down (let go and they hold their height). Planes: Space brakes on the ground |
| B | Drop a bomb: the yellow diamond shows where it will land |
| F or X | Fire a missile: optical ones fly to your crosshair, so keep it on the target; radar ones chase the aircraft in the red box |
| Q | Cycle AP, HE and FLAK |
| V | VTOL: switch between hovering and flying on the wings |
| R | Level out in the air (or flip upright on the ground) |

Taking off: open the throttle (hold W), let the plane run down the strip, and pull the mouse up once the HUD says you're fast enough. Land the plane back at the south end of the strip, or a helicopter on the helipad, to rearm.

**Anywhere**

| Do this | To |
|---|---|
| L | Live fire: the parked vehicles' crews shoot back |
| M | Tactical map, from a vehicle with a command console (Corsair, Zephyr, Colossus) or at the command post by the depot. Click or drag to pick units, right click to attack / escort / move them, Shift+right click to patrol, H hold, F formation, Ctrl+1-9 / 1-9 groups, J jump into one, Deploy calls more in |
| F8 | Battle drill: two armies of about 100 vehicles fight across the range. Again for about 200 (a battleship each side), again to clear |
| F3 | Performance readout: frame rate, draw calls, what's on the field, and where each frame's time goes |
| Backspace | Respawn (vehicle and you) |
| T | Reset the targets |
| Tab | Back to the builder |
| Esc / F1 | Free the mouse / help |

The circles on screen show where each gun will actually land: green when it's loaded. Every hit is listed in the top right:
- **PEN**: the shell got through. It shows the shell's penetration vs. the armor.
- **NO PEN**: the armor stopped it. Angled armor counts as thicker.
- **RICOCHET**: the shell hit at a steep angle and bounced off.

The range is an island. It has walls of each armor type (one angled), a bunker with ammo inside, two parked vehicles, pop-up targets for the rifle, slopes at 12°, 22° and 32°, and a wall at 300 m. A beach on the east coast runs down into open sea, where an enemy patrol boat lies broadside on and a submarine sits on the surface further out. In the western hills there's an airstrip and a helipad. A target plane flies circuits round the island, a helicopter hovers over the eastern hills, and an airship floats off the north-east coast.

Everything you hear is generated by the game itself (no sound files): gun blasts, impacts, ricochets, explosions, the engine and the builder's clunks. Distant explosions arrive late, the way sound really does.

## What to test (and tell Claude about)
- **Building:** Is placing, rotating and removing blocks smooth? Is anything confusing?
- **Driving:** Do tanks and the wheeled scout feel heavy and controllable? Does anything bounce, sink or slide?
- **Turrets:** Do they turn toward the crosshair, and do the circles match where shells land?
- **Destruction:** Shoot the parked tank's turret ring, its ammo, its barrel. Do pieces break off the way you'd expect?
- **Speed:** Is anything slow (frame rate drops during big explosions, or when lots of debris is around)?
- **On foot:** Does walking, shooting and welding feel right? Can you get in and out of your vehicle cleanly?
- **Crew:** Turn on live fire (L), drive the Warden, and press G so your gunner picks its own targets. Does the fight feel fair?
- **Sea:** Load the Corsair and press Tab. Does it sit on the water and feel heavy but responsive? Torpedo the enemy boat (F), then shell it below the waterline: does it settle and sink the way you'd expect? Then try the Barracuda: dive (C), come up (Space), and see whether depth keeping feels right.
- **Swimming:** Walk off the beach. Does swimming and climbing back onto a boat work?
- **Flying:** Load the Kestrel. Can you take off from the strip, fly where you point, and bomb the bunker using the diamond? Is it too easy or too twitchy? Then the Hornet: does it hover steadily, and do its missiles go where you aim?
- **Flak:** Load the Skyguard, press Q until FLAK is picked, turn on live fire (L), and shoot at the circling plane. Do the bursts feel right?
- **Drones:** Load the Wasp, get out, look at it and press E. Flying it while your body stands there should feel odd in a good way.
- **Big machines:** Load the Colossus in the builder. Does orbiting and building on it stay smooth? Try the box tools on it. Then press Tab: does it sit low and steady, and take a few minutes to come round with the rudder hard over? What frame rate do you get?
- **Broadside:** In the Colossus, aim out to one side and click. Do the turrets come round and fire as they bear, and do the guns on the far side hold fire? Is the blast too much or not enough? Hold the button to keep the flak firing: does the frame rate hold up?
- **Flak watch:** Turn on live fire (L), press G so the gunners pick their own targets, and watch whether the flak guns go after the circling plane while the big guns stay on ships.
- **Looks:** Do hulls read as smooth plate now? Do tracks, wheels, guns, propellers and engines look right up close? Does anything flicker, show gaps, or look wrong where a block has been shot off and welded back?
- **Shapes and paint:** in the builder, pick Heavy Armor and press V a few times: does each shape go where you expect when you rotate it? Load the Warden: the shallow glacis, rounded turret and woodland paint should look like one tank. Try a few paint jobs from the top bar, and load the Corsair to see the ship rules (red below the waterline, grey decks).
- **Cockpits:** load the Kestrel and look at the canopy over the pilot's seat. Then try building your own from glazing (slopes and corners with V). Does anything look out of place?
- **Details and joints:** the Warden now has hatches, smoke launchers, spare tracks, stowage boxes and exhausts; wheels have axles to the hull. Add some details to your own designs: is it clear how to turn them onto walls?
- **Copy and paste:** Ctrl+C, drag a box round a turret, then Ctrl+V and click to paste a second one. Turn it with R, flip it with H. Does it feel quick? Save it as a part and load it into another design.
- **Armor view:** press O on the Warden. Can you see where it's thin?
- **Fleet command:** start the battle drill (F8), walk to the command post by the depot (on foot: E to get out) and press M. Drag a box round some blue tanks and right click near the red ones: do they move up in a line? Right click a red tank you can see to focus fire on it. Try a wedge (F), a patrol (Shift+right click), hold fire, then J to jump into one. Call in a few of your own designs with Deploy. Is anything confusing or fiddly?
- **Battle drill:** press F3, then F8. Note the frame rate once everyone has deployed and the fighting starts, and again with F8 a second time (about 200 vehicles). Screenshots of the F3 readout at the worst moment are the most useful thing you can send. Walk up to a blue tank and press E to join in.
- **Far models:** in the drill, look down the lines at the tanks 200 m and further away. Do they look right, or do you notice them switching? Zoom in (right click) on a far one: it should switch back to full detail.
- **Jet fighter:** load the Falcon, hold W to full throttle and take off. Do the afterburner flame and the HUD note show? Does it feel fast but still controllable? Watch the fuel go.
- **Railgun:** load the Vanguard and shoot at the far targets. The slug should fly almost flat and punch through anything. Watch the Power line: fire quickly and it runs down to "CHARGING".
- **Rocket artillery:** load the Hailstorm, turn the launcher toward something far off, aim at the ground and click. Does the salvo ripple off and land as a spread? Does it feel like artillery?
- **Destroyer:** load the Trident and sail out. Aim at the island and press F: a strike missile climbs out of its cell and flies itself there. Aim at the circling plane and press F again: an air-defense missile goes after it. Turn on live fire (L): do the CIWS and the laser shoot down what's thrown at you?
- **Big booms:** shoot a loaded VLS cell or the Trident's reactor (it's deep inside). Is the blast big enough? Too big? Does the shake feel right?
- **Interiors:** walk into the command post by the depot. Does the door open as you walk up and close behind you? Climb the stairs and the ladder to the roof. Do the windows look right from inside?
- **Moving decks:** board the Trident or Corsair, have the AI drive (or set the telegraph and get out), then walk around the deck while she's moving and turning. You should stay put relative to the deck.
- **Crew stations:** on a ship, walk up to a gunner seat and press E: you should take that seat, not the driver's.
- **Buildings:** walk over to your outpost east of the depot. Rest on a bunk (E), get hurt (live fire, L), and see that you come back at the bunk. Stand by the first aid and the weapon locker. Drive a damaged tank onto the apron and park: it should be rearmed and repaired. Climb the stairs and man the roof gun (E at its seat). Then shell the enemy pillbox down range: it should fight back through its slit.
- **Heavy weapons:** walk to the siege battery behind the outpost, take the gunner seat and lob a shell down range. Then load the Juggernaut: try firing on the move (it shouldn't: "STOP TO BRACE"), stop, watch the stabilizers swing down, and fire. Its reload should be twice the battery's. At the coastal fort, take a gunner seat on the roof and try the heavy railgun; with live fire on, watch the laser battery swat missiles and aircraft.
- **Building your own:** lay a floor of Foundation, put up Concrete walls, add a door, a gunner seat and a gun. The dashboard should say "Building".
- **Fittings:** in the Warden, press Z for smoke (turn on live fire first: the enemy shouldn't be able to see you through it) and H for the lights. Let it sit unhit for a while after damage: the crew should patch it up and refit a lost track. Get out at the turret and walk to a hatch: E should put you in the seat beside it.
- **Walk through the Trident:** in at the bridge door, down the ladder in the floor, through the door into the reactor room. Do the lamps come on as you go?
- **Fire:** shoot a parked vehicle's fuel tanks or ammo without destroying them (L for live fire helps). Fires should start, smoke, spread, and eventually burn out or cook off. Get out with the welding torch (3) and point it at the fire to put it out.
- **Speed check:** ships and aircraft now reach the top speeds the dashboard shows (they were being held back). Does the Kestrel feel faster and climb better? Does the Corsair still feel right?

Screenshots, or the text from Godot's **Output** panel at the bottom when something goes wrong, help a lot.

## For developers
- `src/Core/`: the game's rules in plain C#, with no Godot dependency (blocks and big-block footprints, blueprints, gun math, penetration, connectivity, design analysis, hydrostatics, buoyancy bricks, flooding and aerodynamics). Block models (`BlockModels.cs`) and the hull surface mesher (`HullMesher.cs`) live here too, as plain shape and triangle data.
- `src/Game/`: the Godot side (rendering, physics bodies, input, UI, effects). `src/Game/Audio/Synth.cs` builds every sound from scratch.
- `assets/fonts/`: Barlow and Chakra Petch (SIL Open Font License, included).
- Tests: `dotnet test tests/DreamGame.Core.Tests`.
- Build check without the editor: `dotnet build DreamGame.csproj`.
