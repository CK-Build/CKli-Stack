# Documentation guidelines

What is specific to the documentation of the CKli stack. They apply to people and to coding agents alike.

**The generic rules come first:** where a document lives, describing the current state only, grounding every claim in
the source, the style, the links between repositories. They are the CK stack's
[documentation guidelines](https://github.com/signature-opensource/CK-Stack/blob/main/Docs/Guidelines/documentation.md):
read them before writing or reviewing a README here. This document only adds what is specific to CKli.

## The documentation set

- **A `README.md` next to every `.csproj`**: `CKli.Core`, `CKli.Loader`, `CKli.Plugins.Core`, `CKli.Testing`, each
  `StandardPlugins/CKli.*.Plugin`, `CK.SVersion`, `CK.Packaging.Abstractions`. Each one explains WHY the project exists
  and HOW it works (key types, commands, configuration, event hooks).
- **A plugin README also shows how the plugin is configured** in a World's `<Plugins>` element, copied from a real
  World file or from the plugin's configuration code, and the hooks it subscribes to or exposes.
- **`CKli-Plugins/` has a README next to its `.slnx`** that explains its role of plugin development and test harness.
- **`.PublicStack/README.md` is the map** of every README of the stack.

## Where a command is documented

- **An intrinsic command** (a `Command` class in `CKli.Core/CKliCommands/`) is documented in
  `CKli.Core/CKliCommands/README.md`, one `###` heading per command that carries the command and its flags (a new flag
  goes in the heading too).
- **A plugin command** (`build`, `publish`, `branch open`...) is documented in its plugin's README, in its
  `### Commands` section.
- **`CKli/README.md` is a map, not a reference:** its "The commands" section has one row per depth-1 command or
  namespace, with the entries, order and summaries of `ckli --help`, and links to the reference. It changes only when an
  entry appears, disappears or changes its summary.
- **`CKli.Core/README.md` documents the API, not the command line:** updating it is not updating the command
  reference.
- **Flags are spelled as the command registry spells them.** Before calling a flag undocumented, search both
  spellings: some option tables list an option by its parameter name (`allowDowngrade`, `noFetch`).

## Links in this stack

`CKli-Plugins/` is a folder of the `.PublicStack` repository: its links to `../README.md` or `../CKli.xml` stay
relative. Its links into `CKli`, `CK-SVersion` or `CK-Packaging-Abstractions` cross repositories: they use the origin
URLs, on `stable`.
