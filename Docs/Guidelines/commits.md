# Commits

Rules for the content and the messages of the commits in the CKli stack. They apply to people and to coding agents
alike. Where a commit lands is in [the branching guidelines](branching.md).

## What a commit contains

- **A commit touches `StandardPlugins/` or everything else, never both.** A change that spans both is split: the core
  commit first (the plugins depend on `CKli.Plugins.Core`). An intermediate commit that doesn't compile is accepted.
- **The test changes of `Plugins.Tests` are a separate commit** in `.PublicStack` (see
  [the branching guidelines](branching.md#branch-details)).
- **The encoding of the files is set by `.editorconfig` and `.gitattributes`** (UTF-8 without a BOM, with CRLF line
  endings): never override them.
- **A split commit is a real state:** build and run the suite on each intermediate tree before committing it, so that
  `git bisect` lands on something that existed.

## The messages are parsed for the next version

The version change of a build is the maximum found in the messages of the commits since the last version
(`TagCommitTree.GetVersionChange` in `CKli.VersionTag.Plugin`, the only commit message parser of the stack):

| In the message | Version change |
|---|---|
| `BREAKING CHANGE` or `BREAKING-CHANGE` anywhere (case-sensitive) | Major |
| a header `type(scope)!:` (the `!`) | Major |
| a header `feat:` or `feat(scope):` | Minor |
| any other header type, or no header | Patch |

- **The breaking marker fires on a mere mention:** never write it in a message that is not breaking.
- **Rewriting history keeps every marker**, on the commit that contains the breaking change. Squashing keeps the
  strongest one. Adding a marker can only raise the version: ask before marking a range that is already released.

## What a message says

- **The subject names the change** ("Version tags carry a `building/` or `local/` prefix"), not the activity ("Huge
  refactoring"). Imperative mood.
- **The body gives the WHY**, which otherwise lives nowhere but in the diff.
- **A dependency transition is recorded** (`CK.SVersion 0.2.2 -> 0.2.3`).
