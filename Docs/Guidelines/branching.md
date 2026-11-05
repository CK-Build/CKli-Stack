# Branching guidelines

Rules for committing in the repositories of the CKli stack. They apply to people and to coding agents alike.

## Never commit to a bare branch

**No commit ever lands in `stable`: it lands in `dev/stable`.** The bare branches are integration targets: CKli
integrates the `dev/` branch when it builds or publishes, so a commit on `stable` is outside what the tooling picks up.

**Exception: the Stack repository (`CKli-Stack`, cloned as `.PublicStack`) commits on `main`.**

Before the first commit of a session, check the current branch. If it is bare, create (or switch to) its `dev/`
counterpart first:

```
git rev-parse --abbrev-ref HEAD      # e.g. "stable"
git checkout -b dev/stable           # or: git checkout dev/stable, if it exists
```

## Branch details

- **The default branch of `CKli`, `CK-SVersion` and `CK-Packaging-Abstractions` is `stable`** (`main` for the Stack
  repository): base a pull request on it. Don't infer it from `origin/HEAD`, which can be stale: the convention wins.
- **`stable`'s commits are not a precedent.** They arrived by integration: a non-CI build or publish carries
  `dev/stable` onto `stable` and deletes the `dev/` branch. A missing `dev/stable` means "nothing in flight", not
  "commit on `stable`".
- **`CKli-Plugins/` is a folder of `.PublicStack`, not a repository.** A change of its tests (`Tests/Plugins.Tests`)
  commits on `.PublicStack`'s `main`. A plugin change and its integration tests are always at least two commits in two
  repositories.
- **`ckli build --regular` integrates `dev/stable`:** it moves `stable` to its tip, deletes `dev/stable` and commits
  "Updated dependencies." on `stable`. Afterwards you are on `stable`: `git checkout -b dev/stable` before the next
  commit. `build` and `publish` run in CI by default, and a CI build integrates nothing.
- **`dev/stable` is transient:** never reference it durably (a documentation link, a script, a CI configuration). Use
  `stable`.
- **The `fix/vMajor.Minor` branches belong to `ckli fix`**
  ([Fix-Workflow.md](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/Fix-Workflow.md)): never create them
  by hand. The branch model (the `stable` root, the prerelease branches, `explo/`) is described in
  [HotZone-Workflow.md](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/HotZone-Workflow.md).

## Repairing commits made on a bare branch

As long as nothing is pushed, move the references (no reset, no file touched):

```
git branch dev/stable <tip>
git checkout dev/stable
git branch -f stable <upstream>
```
