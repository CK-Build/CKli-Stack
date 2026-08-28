# CKli-Plugins

**CKli-Plugins** is the development and integration-test harness for CKli's **Standard Plugins** — the
first-party, built-in plugins (`CKli.*.Plugin`) that ship with [CKli](https://github.com/CK-Build/CKli)
itself and implement its default .NET-repository automation (branching, building, versioning,
publishing, etc.).

The actual plugin **source code does not live here**. It lives in the host `CKli` repository, under
[`StandardPlugins/`](https://github.com/CK-Build/CKli/tree/stable/StandardPlugins). This repository only *references* that source (via
`ProjectReference`, see [`CKli.Plugins/CKli.Plugins.csproj`](CKli.Plugins/CKli.Plugins.csproj)) and adds
the integration tests that exercise it end-to-end.

## Why this repo exists

CKli.Core's plugin loading model (see the [CKli.Core README](https://github.com/CK-Build/CKli/blob/stable/CKli.Core/README.md#plugin-system))
expects, for a World named `W`, a `{W}-Plugins` solution folder sitting next to the World's Stack
checkout — either containing source-based plugin projects, or referencing packaged ones. This repository
**is** that `{WorldName}-Plugins` folder for the `CKli` World defined by
[`../CKli.xml`](../CKli.xml) (see the [Stack-level README](../README.md) for the full picture).

In other words: rather than the Standard Plugins being tested from *inside* `CKli.sln` as if they were
just another library, they are tested the same way any third-party or Stack-local plugin would be —
loaded, compiled and driven through real `ckli` commands by CKli itself. This is how CKli dogfoods its
own plugin architecture and standard plugin set.

## The 9 Standard Plugins

| Plugin | What it does | README |
|---|---|---|
| `CKli.ArtifactHandler.Plugin` | Manages the local NuGet feed / asset storage under `$Local/<World>/`, keeps every repo's `nuget.config` in sync with the World's configured feeds, and defines the `BuildContentInfo` format stored in release tags. | [→](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.ArtifactHandler.Plugin/README.md) |
| `CKli.BranchModel.Plugin` | Implements CKli's "hot branch model": a stable branch, ordered CSemVer prerelease branches and `explo/` branches, each paired with a `dev/` working branch. Detects/fixes branch-structure issues and exposes `branch open/close/switch/sync` + `commit` commands. | [→](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.BranchModel.Plugin/README.md) |
| `CKli.Build.Plugin` | Orchestrates build → test → pack → publish across the World's repository dependency graph (the "Roadmap"), including the Fix Workflow for patching already-released versions. Implements the `*publish`/`*build` commands. | [→](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Build.Plugin/README.md) |
| `CKli.CommonFiles.Plugin` | Reconciles shared template files (e.g. `.editorconfig`, CI config fragments) across all repos of a World via the branch model's content-issue detection. | [→](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.CommonFiles.Plugin/README.md) |
| `CKli.HotZone.Plugin` | Computes the cross-repo dependency/build-order graph over the "hot zone" (commits since the last stable release) and implements the Fix Workflow (`fix start/info/cancel`) for patching and propagating fixes downstream. | [→](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.HotZone.Plugin/README.md) |
| `CKli.Migration.Plugin` | A transient, "very optional" plugin holding one-off migration utilities for converting repos from CKli's older (Net8-era) conventions to the current ones. Not meant to stay enabled once a Stack is converted. | [→](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Migration.Plugin/README.md) |
| `CKli.Publish.Plugin` | Event-driven (no commands of its own): hooks into `Build.Plugin`'s build events to push NuGet packages and create GitHub/GitLab/Gitea releases once a build succeeds. | [→](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Publish.Plugin/README.md) |
| `CKli.ShallowSolution.Plugin` | Fast, checkout-free XML analysis and mutation of `.slnx` solutions directly from Git commits/trees — used to inspect and rewrite package versions without building. | [→](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.ShallowSolution.Plugin/README.md) |
| `CKli.VersionTag.Plugin` | Reconciles raw Git tags into a version history per repo (CSemVer, `building/`, `local/`, `+fake`, `+deprecated`, `--ci.0` conventions) and answers "what's the next version to build". Exposes `version bump`/`version deprecate`. | [→](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.VersionTag.Plugin/README.md) |

These plugins depend on each other quite heavily (e.g. `Build` drives `Publish` and `VersionTag`;
`BranchModel` is extended by `CommonFiles` and `ShallowSolution`; `HotZone` and `Migration` both sit on
top of several others) — see each plugin's own README for its specific relationships.

The shared contract layer they're all built against is
[`CKli.Plugins.Core`](https://github.com/CK-Build/CKli/blob/stable/CKli.Plugins.Core/README.md), loaded via
[`CKli.Loader`](https://github.com/CK-Build/CKli/blob/stable/CKli.Loader/README.md); both are part of `CKli.Core`
(see its [Plugin System section](https://github.com/CK-Build/CKli/blob/stable/CKli.Core/README.md#plugin-system)).

## How this solution is wired

- **[`CKli.Plugins/CKli.Plugins.csproj`](CKli.Plugins/CKli.Plugins.csproj)** — the plugin-solution's
  aggregator project. It has `ProjectReference`s to `CKli.Plugins.Core` and to all 9 Standard Plugins
  above (mirroring what `CKli.CompiledPlugins.cs`, below, and the host's own
  `StandardPlugins/CKli.Plugins` aggregator, both do for `dotnet pack`ing purposes).
- **[`CKli.Plugins/CKli.Plugins.cs`](CKli.Plugins/CKli.Plugins.cs)** — the reflection-based fallback
  entry point (`Plugins.Register(PluginCollectorContext)`), used by `CKli.Loader` whenever no valid
  compiled adapter exists yet.
- **`CKli.Plugins/CKli.CompiledPlugins.cs`** — the compiled, code-generated fast-path adapter. It is
  **auto-generated** by `ckli plugin compile` and is git-ignored (see [`../.gitignore`](../.gitignore));
  it is regenerated automatically whenever the plugin set's signature changes. Do not edit it by hand.
- **[`Common/`](Common)** — exists purely so `CKli.Core`'s `Directory.Build.props` resolves correctly
  when the Standard Plugins are compiled from this solution instead of from `CKli.sln` — see
  [`Common/README.md`](Common/README.md).
- **[`Tests/Plugins.Tests/`](Tests/Plugins.Tests)** — the integration test suite. It references
  [`CKli.Testing`](https://github.com/CK-Build/CKli/blob/stable/CKli.Testing/README.md) and this solution's `CKli.Plugins.csproj`, then
  drives real `ckli` command sequences (World init, branch open/close/sync, HotZone fix workflow,
  version bumps, sample package publishing, coworking scenarios, ...) against fake local Git remotes
  (`Remotes/`) and reproducible fixtures (`Cloned/`) — no network access or real Git hosting involved.

## See also

- [`.PublicStack/README.md`](../README.md) — the Stack this plugin solution belongs to.
- [`CKli` (host tool) README](https://github.com/CK-Build/CKli/blob/stable/README.md) — the `ckli` command reference.
- [`CKli.Core` README](https://github.com/CK-Build/CKli/blob/stable/CKli.Core/README.md) — Stack/World/Repo model and the plugin architecture these plugins implement against.
