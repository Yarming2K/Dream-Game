# Notes for Claude

- Game design lives in `docs/DESIGN.md`. Check it before adding features, and update it when a design decision changes.
- Engine: Godot 4.7.2 (.NET build), C#, .NET 8, Jolt physics.
- `src/Core/` is plain C# with **no Godot references**. It holds the rules of the game (blocks, blueprints, stats, ballistics, damage, connectivity). Keep it deterministic and unit tested; it's what multiplayer and simplified far-off battles will build on.
- `src/Game/` is the Godot side: nodes, physics bodies, rendering, input, UI. It turns Core results into things on screen.
- Tests: `dotnet test tests/DreamGame.Core.Tests`. The Godot project compiles with `dotnet build DreamGame.csproj`.
- UI and scenes are built in code where practical, so changes are reviewable as text.
- The owner directs and playtests; they don't write code. Explain changes in plain language and say what to try in-game.
