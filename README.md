# CKli-Stack

This folder is the local checkout of the **[`CKli-Stack`](https://github.com/CK-Build/CKli-Stack)**
Git repository — a CKli **Stack**: the control-plane repository that defines a group of repositories
(a **World**) and the plugins that automate work across them. It is itself managed by the very tool
(`ckli`) whose own source code it happens to contain.

For the general Stack / World / Repo model, see the
[**CKli.Core README**](https://github.com/CK-Build/CKli/blob/stable/CKli.Core/README.md#core-concepts-stack--world--repo). This document is
about what's specifically inside *this* Stack, and is meant as the entry point into the rest of the
documentation set below.

## This Stack's World

The default (and only) World is **`CKli`**, defined by [`CKli.xml`](CKli.xml):

```xml
<CKli MinCKliVersion="0.10.0--ci.28">
  <Plugins CompileMode="Debug">
    <ArtifactHandler> <!-- NuGet feeds: nuget.org, Signature-OpenSource --> </ArtifactHandler>
    <BranchModel /> <Build /> <CommonFiles /> <HotZone /> <Migration />
    <Publish /> <ShallowSolution /> <VersionTag />
  </Plugins>
  <Repository Url="https://github.com/CK-Build/CKli" />
  <Repository Url="https://github.com/CK-Build/CK-SVersion" />
</CKli>
```

It manages 2 repositories, cloned as siblings of this `.PublicStack/` folder (at the Stack root):

| Repo | Local path | What it is |
|---|---|---|
| [`CK-Build/CKli`](https://github.com/CK-Build/CKli) | `../CKli/` | The CKli tool itself: `CKli.Core`, `CKli.Loader`, `CKli.Plugins.Core`, `CKli.Testing`, the `CKli` CLI, and the source of the 9 Standard Plugins. |
| [`CK-Build/CK-SVersion`](https://github.com/CK-Build/CK-SVersion) | `../CK-SVersion/` | A dependency of CKli (CSemVer-related versioning library). |

...and it enables all 9 Standard Plugins shipped with CKli, in `Debug` compile mode — see
[`CKli-Plugins/README.md`](CKli-Plugins/README.md) for what each one does.

## Why the Standard Plugins exist

The 9 Standard Plugins are not a loose bag of independent utilities. Together, they exist to implement
two deliberately distinct release workflows:

- **The HotZone Workflow** — the regular, day-to-day `build`/`publish` flow. Given a target branch, it
  walks the World's entire dependency graph, rebuilds whatever actually needs rebuilding because
  something it depends on changed, and publishes the result — live, and transitively across the World.
- **The Fix Workflow** (`fix start` / `fix build` / `fix publish`) — patches an already-superseded
  release (something behind the current hot zone) in isolation, deliberately pinned against anything
  that has changed on the hot line since the original release.

Every plugin's role ultimately serves one or both of these: `BranchModel` and `ShallowSolution` provide
the branch/version machinery both workflows resolve dependencies through, `VersionTag` and
`ArtifactHandler` track releases and artifacts, `Build`, `HotZone` and `Publish` implement the two
workflows themselves, and `CommonFiles`/`Migration` are supporting, maintenance-oriented concerns.

See **[`HotZone-Workflow.md`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/HotZone-Workflow.md)** and
**[`Fix-Workflow.md`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/Fix-Workflow.md)** for the full explanation of each
workflow, including the correctness invariant each one relies on — this is the best starting point for
understanding *why* the Standard Plugins are shaped the way they are.

## Folder layout

```
.PublicStack/                ← This Stack's git working directory (this folder).
├── $Local/                  ← Local, gitignored scratch space (NuGet feed cache, asset storage, ...).
├── CKli-Plugins/             ← The "{WorldName}-Plugins" solution: dev/test harness for the 9 Standard
│                                Plugins referenced by the CKli World above. See its own README.
├── Common/                  ← Shared MSBuild props/files consumed by the CKli-Plugins solution.
├── Logs/                    ← Per-stack CKli command logs (gitignored).
└── CKli.xml                 ← The "CKli" World definition file shown above.

../CKli/                     ← Cloned Repo: the CKli tool itself (sibling of .PublicStack/).
../CK-SVersion/               ← Cloned Repo: CK-SVersion (sibling of .PublicStack/).
```

## Documentation map

A guide to every README in this Stack, roughly host-tool-first then plugins:

**The CKli tool** (`../CKli/`, repo `CK-Build/CKli`):
- [`CKli` — command reference](https://github.com/CK-Build/CKli/blob/stable/README.md) — the `ckli` CLI itself: `clone`, `pull`, `push`, `plugin ...`, `tag ...`, etc.
- [`CKli.Core`](https://github.com/CK-Build/CKli/blob/stable/CKli.Core/README.md) — the core library: Stack/World/Repo model, Git hosting providers, the plugin system, command dispatch.
- [`CKli.Loader`](https://github.com/CK-Build/CKli/blob/stable/CKli.Loader/README.md) — the collectible `AssemblyLoadContext` used to hot-load and unload compiled plugin assemblies.
- [`CKli.Plugins.Core`](https://github.com/CK-Build/CKli/blob/stable/CKli.Plugins.Core/README.md) — the shared contract/runtime library between `CKli.Core` and plugin assemblies (reflection-based and compiled/generated discovery).
- [`CKli.Testing`](https://github.com/CK-Build/CKli/blob/stable/CKli.Testing/README.md) — test helpers for exercising real `ckli` commands against fake local Git remotes.

**The 9 Standard Plugins** (`../CKli/StandardPlugins/`), and the harness that builds/tests them here:
- [`HotZone-Workflow.md`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/HotZone-Workflow.md) — the regular `build`/`publish` workflow and its branch-compatibility invariant. **Start here** to understand why the plugins below exist.
- [`Fix-Workflow.md`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/Fix-Workflow.md) — the `fix start`/`fix build`/`fix publish` workflow for patching a superseded release.
- [`CKli-Plugins`](CKli-Plugins/README.md) — this World's `{WorldName}-Plugins` solution: what it's for and how it's wired, with links to each plugin below.
  - [`CKli.ArtifactHandler.Plugin`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.ArtifactHandler.Plugin/README.md)
  - [`CKli.BranchModel.Plugin`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.BranchModel.Plugin/README.md)
  - [`CKli.Build.Plugin`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Build.Plugin/README.md)
  - [`CKli.CommonFiles.Plugin`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.CommonFiles.Plugin/README.md)
  - [`CKli.HotZone.Plugin`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.HotZone.Plugin/README.md)
  - [`CKli.Migration.Plugin`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Migration.Plugin/README.md)
  - [`CKli.Publish.Plugin`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.Publish.Plugin/README.md)
  - [`CKli.ShallowSolution.Plugin`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.ShallowSolution.Plugin/README.md)
  - [`CKli.VersionTag.Plugin`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.VersionTag.Plugin/README.md)
- [`CKli-Plugins/Common`](CKli-Plugins/Common/README.md) — why that folder exists (MSBuild props resolution for the plugin solution).

Start with `CKli.Core`'s README for the concepts (Stack/World/Repo, plugins, commands); everything else
builds on it.
