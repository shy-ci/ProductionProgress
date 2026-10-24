# Menu integration regression checks

Run from the repository root with .NET 8:

```sh
dotnet run --project checks/MenuIntegration/MenuIntegrationCheck.csproj -c Release
```

This executable links the real `MenuIntegration.cs` with recording UI doubles. It never loads game or third-party mod binaries. The stub `ProgressPage` does not validate list drawing or held scrolling.

Coverage includes native-root rendering, visual slot 2 versus the appended internal index, letters/child menus, diagnostics, native tab switching with proxy streaming enabled, arbitrary neighboring page replacement, multiple appended pages, resize/map-return notification order, and rejection of invalid owned pages/mappings/overlaps. Trigger checks include shallow press, deep press, partial release, holding, and complete release, modeling SMAPI 4.0.8 override retention separately from native digital edges. Third-party lifecycles are modeled from reported state and inspected source; these checks do not replace a game session with the actual mod combination.
