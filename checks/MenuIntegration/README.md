# Menu integration regression checks

Run from the repository root with .NET 8:

```sh
dotnet run --project checks/MenuIntegration/MenuIntegrationCheck.csproj -c Release
```

This executable links the real `MenuIntegration.cs` with recording UI doubles. It never loads game or third-party mod binaries. The stub `ProgressPage` does not validate list drawing or held scrolling.

It also links the optional Better Game Menu adapter and its minimal public API contract. Checks cover absent/unavailable APIs, registration placement, farm visibility, initial and cached-page activation, independent menu instances, icon bounds, cleanup, and native input/drawing ownership when the root switches hosts. The actual SMAPI API proxy and third-party rendering require the separate cloud game test.

Keep all members of `ITabChangedEvent`, including `OldTab`, even though the adapter only reads `Menu` and `Tab`. SMAPI 4.5.2's Pintail delegate conversion validates the reverse interface shape too. Removing `OldTab` caused the real API proxy to fail before registration; a local check with that SMAPI version's bundled Pintail reproduced the failure and verified the complete shape. The recording host alone cannot detect this cross-assembly contract error.

Coverage includes native-root rendering, visual slot 2 versus the appended internal index, letters/child menus, diagnostics, native tab switching with proxy streaming enabled, arbitrary neighboring page replacement, multiple appended pages, resize/map-return notification order, and rejection of invalid owned pages/mappings/overlaps. Trigger checks include shallow press, deep press, partial release, holding, and complete release, modeling SMAPI 4.0.8 override retention separately from native digital edges. Third-party lifecycles are modeled from reported state and inspected source; these checks do not replace a game session with the actual mod combination.
