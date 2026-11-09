# Dream Game

A military-industrial sandbox: design everything, then fight a war for a star system. Working title.

The scope we agreed on is in [docs/DESIGN.md](docs/DESIGN.md). Built so far: **milestone 1** (the builder and test range), **milestone 2** (on foot, crew and field repair) and the **sea front** of milestone 3 (ships, submarines and torpedoes). Air and colossal machines are next.

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
| Middle click | Copy the block under the cursor |
| R / T / G (Shift reverses) | Rotate the block |
| M | Mirror mode (on at start) |
| 1-9, [ and ] | Pick a block, switch category |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Right drag, middle drag, wheel | Orbit, pan, zoom |
| W A S D, Q / E | Move the camera around, down / up |
| F | Focus the camera |
| Ctrl+S | Save |
| Tab | Test drive |
| F1 | Help |

The **parts drawer** on the left shows every block with a thumbnail; the card under it lists the selected part's stats. The **dashboard** on the right updates as you build: mass, speed, armor bars (the tick marks the weakest spot), gun stats and a list of problems in plain English. The pink dot is the center of mass. Blocks tinted red aren't attached and would fall off.

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
| R | Put the boat back upright |
| E | Get out onto the deck |

Holes below the waterline flood the hull: the HUD shows each compartment filling. Get out, weld the holes shut, and the bilge pumps clear the water. Stop inside the **buoy ring** to rearm and refuel. In the water you swim: Space up, C down, and E climbs into a boat you're looking at.

**Anywhere**

| Do this | To |
|---|---|
| L | Live fire: the parked vehicles' crews shoot back |
| Backspace | Respawn (vehicle and you) |
| T | Reset the targets |
| Tab | Back to the builder |
| Esc / F1 | Free the mouse / help |

The circles on screen show where each gun will actually land: green when it's loaded. Every hit is listed in the top right:
- **PEN**: the shell got through. It shows the shell's penetration vs. the armor.
- **NO PEN**: the armor stopped it. Angled armor counts as thicker.
- **RICOCHET**: the shell hit at a steep angle and bounced off.

The range is an island. It has walls of each armor type (one angled), a bunker with ammo inside, two parked vehicles, pop-up targets for the rifle, slopes at 12°, 22° and 32°, and a wall at 300 m. A beach on the east coast runs down into open sea, where an enemy patrol boat lies broadside on and a submarine sits on the surface further out.

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

Screenshots, or the text from Godot's **Output** panel at the bottom when something goes wrong, help a lot.

## For developers
- `src/Core/`: the game's rules in plain C#, with no Godot dependency (blocks, blueprints, gun math, penetration, connectivity, design analysis, hydrostatics and flooding).
- `src/Game/`: the Godot side (rendering, physics bodies, input, UI, effects). `src/Game/Audio/Synth.cs` builds every sound from scratch.
- `assets/fonts/`: Barlow and Chakra Petch (SIL Open Font License, included).
- Tests: `dotnet test tests/DreamGame.Core.Tests`.
- Build check without the editor: `dotnet build DreamGame.csproj`.
