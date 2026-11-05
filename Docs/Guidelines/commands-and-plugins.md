# Commands and plugins

Rules for adding or changing a `ckli` command, a plugin configuration attribute or a plugin hook. They apply to people
and to coding agents alike. How a command is written (its method signature, its base classes) is in
[CKli.Core's README](https://github.com/CK-Build/CKli/blob/stable/CKli.Core/README.md).

## The core knows nothing about the plugins

**`CKli.Core` holds no plugin convention:** only generic hooks that a plugin opts into. What depends on a kind of
repository content (a .NET solution, NuGet packages, versions) is in a plugin.

## A command change regenerates `CKli.CompiledPlugins.cs`

**A command added or removed, or whose flags, parameters or return type change, regenerates the World's
`CKli.CompiledPlugins.cs`** (git-ignored, in `<World>-Plugins/CKli.Plugins/`): it is the dispatch of the commands.
Nothing detects a stale one: it usually still compiles, and binds each flag to the wrong parameter.

```powershell
Remove-Item .PublicStack\CKli-Plugins\CKli.Plugins\CKli.CompiledPlugins.cs
& .\CKli\CKli\bin\Debug\net10.0\CKli.exe plugin compile --mode Debug --skip-pull-stack
```

(From the stack root.) Never edit the file, never read it as the reference of a command's shape.

## Changing a command

- **Renaming the C# method is safe.** Changing the `CommandPath`, the name or order of a required parameter or an
  `OptionName` breaks the command line: search the tests and the documentation for the literal strings first.
- **A command is documented** where [the documentation guidelines](documentation.md#where-a-command-is-documented) say,
  and has an integration test in `Plugins.Tests`.

## Plugin configuration attributes

An attribute of a `<Plugins>` child (`<VersionTag AutoFixRemovableTag="true" />`) needs five sites (`VersionTagPlugin`
is the reference): its `XName` in the plugin's `XNames.cs`; its reading in the constructor; its description in
`World.Events.PluginInfo`, in both states (`ckli plugin info` is the only place that lists the supported attributes);
its writing in `OnPluginSetAsync` (`ckli plugin set/unset`); a test in `PluginSetTests`.

- **The configuration element is named after the plugin's assembly**, not its class (`CKli.Publish.Plugin` reads
  `<Publish>`).
- **Never validate (or throw) in a plugin constructor:** a throw degrades the World to "working without plugins",
  which removes the `ckli plugin set` that would fix it. Validate in `OnPluginSetAsync` and where the value is used.
- **The configuration is changed through `PluginConfiguration.Edit`** only, never through the `XElement`.

## Hooks between plugins

- **Reuse an existing hook before adding one** (`BranchModel`'s `ContentIssue`, `Build`'s `OnRoadmapBuild`...).
- **A new hook is a `PerfectEvent`**, never a plain `event Action<...>`: the listener chooses sync, async or parallel.
  Its raiser is async, so the command becomes async.
- **A handler reports a failure explicitly** on its event arguments (`Success`, `SetFailed()`): a handler that only
  logs an error is not caught as a failure.
- **"Behave differently under test" is `CKliRootEnv.IsTestRun`**, never a path or an instance name. Prefer a
  configuration flag when a real user could want the behavior.

## The plugin load context

- **A new `PackageReference` in `CKli.Core` or `CKli.Plugins.Core` is added to
  `PluginLoadContext.GetSharedAssemblies()`.** Otherwise the plugins load their own copy and fail with a
  `MissingMethodException` on a method that plainly exists.
- **The plugins must stay unloadable:** nothing outlives a run that references a plugin or the plugin factory.
