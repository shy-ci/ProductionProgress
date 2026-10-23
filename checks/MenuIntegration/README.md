# Menu integration regression checks

Run from the repository root with .NET 8:

```sh
dotnet run --project checks/MenuIntegration/MenuIntegrationCheck.csproj -c Release
```

This executable links the real `MenuIntegration.cs` and `ProductionMenu.cs` with recording UI doubles. It never loads game or third-party mod binaries. The stub `ProgressPage` does not validate list drawing or held scrolling.

Coverage includes rendering around letters/child menus, diagnostics, native tab switching, Ultra Smooth proxy materialization, SpaceCore's independent `IClickableMenu` skill page, its rebuild on return to a native menu, subscriber ordering, appended options-page removal/reinsertion, resize/reopen, and rejection of invalid identities/mappings/overlaps. Third-party lifecycles are modeled from the reported state and inspected source; these checks do not replace a game session with the actual mod combination.
