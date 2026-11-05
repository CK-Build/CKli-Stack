# Versions and packages

The invariants of the versions and packages that CKli handles. They apply to people and to coding agents alike. Each
plugin's README explains how it implements them.

- **Versions are `CK.SVersion` types** (`SVersion`, `SVersionBound`, `CSVersionKindFilter`): never a hand-rolled
  SemVer. `SVersion` equality ignores the parsed prefix and the build metadata (`local/v1.0.0 == v1.0.0`): use
  `IsLocal()` and `IsBuilding()` to tell them apart.
- **A commit bears at most one version.** A `+fake` tag is the starting version it declares, not a floor.
- **`local/` and `building/` tags are never pushed** (`GitRepository.Push` drops them).
- **A repository references a package identifier in one version only** (a hard error). Different versions in
  different repositories are legal: the build heals them.
- **A package identifier is produced by exactly one repository.**
- **The transitive dependencies of a published profile are observed** from `dotnet package list --include-transitive`,
  never computed by walking the nuspecs, nor trimmed by a target framework list.
- **`BuildContentInfo`'s text is a stored format** (in the annotation of every version tag): only optional sections
  can be appended.
