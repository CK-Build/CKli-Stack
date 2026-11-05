# Building and testing

Rules for building and testing the CKli stack. They apply to people and to coding agents alike. How to run a local
`ckli` is in [CKli's README](https://github.com/CK-Build/CKli/blob/stable/README.md#development--local-testing).

## Building

- **Build `CKli.slnx`, never one project.** The command line references no plugin and a plugin builds only its own
  dependencies: a broken consumer plugin builds fine alone, then fails at `ckli plugin compile`.
- **Never trust a `dotnet test --no-build`** without having seen "Build succeeded" for that same build: it can run a
  stale assembly.
- **`ckli build` and `CompileAndInstallLocalCKli.ps1` are routine** (no confirmation needed).

## `CompileAndInstallLocalCKli.ps1`: the local CI of the whole stack

At the root of the `CKli` repository. It packs `CK-SVersion`, `CK-Packaging-Abstractions` and the `CKli` solution at
`0.0.0-0` into `.local-feed`, reinstalls the global `ckli`, then runs `ckli --version`, `ckli plugin compile` and
`ckli plugin info`: the only end-to-end check of the installed tool.

- **It rewrites the inbound references to `0.0.0-0` and restores them in a `finally`:** an interrupted run can leave
  edits behind. Check `git status` first.
- **`0.0.0-0` is lower than every published version.** NuGet takes the highest version required in the graph, so a
  repository of the stack left unpacked keeps its published version and wins. It never shows as a restore error, but
  as a missing type or member, often at `ckli plugin compile`. **When a repository joins the stack, add it to the
  script:** pack it, force its inbound references, restore them in the `finally`.

## Consuming a new member across repositories

- **A new member of `CK.SVersion` or `CK.Packaging.Abstractions` gives `CS0117` until it is packed and referenced:**
  `CKli.Core` consumes them as packages. This is by design: commit, then `ckli build`. It builds the dependency, moves
  the reference, then builds `CKli`. Never edit the versions by hand.
- **Plugins calling a new `CKli.Core` member: run `CompileAndInstallLocalCKli.ps1` before `ckli build` or
  `ckli publish`.** `CKli.Core` is the running tool, not a package: the installed `ckli` lacks the member
  (`Method not found`). Building `CKli-Plugins.slnx` also overwrites the prebuilt plugin assembly in `$Local` that the
  installed `ckli` loads. So: install the local tool, `ckli publish --regular` with it, then `ckli update` back to the
  published version. One release: no core-only release, no reference surgery.
- **A new `PackageReference` in `CKli.Core` or `CKli.Plugins.Core`** must be added to
  `PluginLoadContext.GetSharedAssemblies()` (see [commands and plugins](commands-and-plugins.md)).

## The test suites

| Suite | What it covers | Cost |
|---|---|---|
| `CKli/Tests/CKli.Core.Tests` | The core, with two sample plugins: **no Standard Plugin** | Cheap: run it whole (`--filter "FullyQualifiedName!~CommonProviderTests"`, that fixture hits GitHub) |
| `CK-SVersion/Tests/CK.SVersion.Tests` | `CK.SVersion` | Cheap |
| `.PublicStack/CKli-Plugins/Tests/Plugins.Tests` | **The only suite that runs the Standard Plugins**: end-to-end `ckli` sequences on fake local remotes | Expensive: filter while iterating, run it whole before committing |

- **A green `CKli.Core.Tests` says nothing about a plugin change.**
- **Prefer the fake-build harness** (`CKli.Build.Plugin.Testing`) to the heavy fixtures: only `dotnet` is faked, and a
  test takes seconds instead of minutes.
- **Never hand-write an expected screen (a golden):** rows carry trailing padding. Capture it from a run, then read
  every captured block against what you predicted: a capture bakes the bugs in too.
- **A wrong `--ci.N` in a golden is a commit-depth bug until proven otherwise:** never refresh it to match the run.

## Running the tests

- **Never run two suites at once, never build while one runs:** the lock contention and the memory pressure turn
  minutes into half an hour, or get the run killed.
- **Never truncate the output of `dotnet test`:** write it to a file, then search `Failed|Error Message`.
- **A command returning `false` explains itself in the log** (`Logs/Text/<timestamp>/*.log`), not in the assertion.
- **Before blaming your change, `git stash` and re-run:** `CommonProviderTests` flakes on GitHub races, and
  `Plugins.Tests` can fail in code it merely traverses.
- **`.PublicStack/CKli.xml`'s `<Plugins>` is inherited by every test clone:** editing it can turn goldens red with no
  code change. Never "refresh" goldens for that.
- **After a killed run, orphan MSBuild nodes hold memory and locks:** `dotnet build-server shutdown`, then build with
  `dotnet build -m:1` before testing.

## Secrets

- **Never run `dotnet user-secrets list`:** it prints the secret values (the PATs), and a leaked PAT must be rotated.
  Read only the keys of `secrets.json`.
- **The stores:** `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`, with the ids `CKli-Test` (`CKli.Core.Tests`),
  `CKli-CKli-Test` (`Plugins.Tests`) and `CKli` (the tool).
- **`FILESYSTEM_GIT` is never in a store:** a fixture whose tests push registers it in its `[OneTimeSetUp]` and clears
  it in its `[OneTimeTearDown]`, per fixture, never per test. A fixture that doesn't push doesn't register it. A
  fixture that passes in a full run but fails alone often lives on what another fixture left in the store.
