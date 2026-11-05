# The documents of the CKli stack

The conventions of the CKli stack, versioned with its Stack repository. They apply to people and to coding agents
alike. What a project does is documented with the project (its `README.md`), not here.

A new guideline adds its row to this table.

## Guidelines

The rules for working in the repositories of the stack ([Guidelines/](Guidelines)).

| Guideline | Read it when |
|---|---|
| [Branching](Guidelines/branching.md) | Before the first commit in a repository: no commit in `stable`, only in `dev/stable` (the Stack repository commits on `main`). |
| [Commits](Guidelines/commits.md) | Committing: `StandardPlugins/` or the rest, the version markers parsed in the messages, what a message says. |
| [Documentation](Guidelines/documentation.md) | Writing or reviewing a README: the documentation set, where a command is documented, the links. |
| [Building and testing](Guidelines/building-and-testing.md) | Building, running a test suite, consuming a new member across repositories, touching a secrets store. |
| [Commands and plugins](Guidelines/commands-and-plugins.md) | Adding or changing a command, a plugin configuration attribute or a hook. |
| [Versions and packages](Guidelines/versions-and-packages.md) | Touching versions, version tags, package references or published profiles. |
| [The .wip folder](Guidelines/the-wip-folder.md) | Keeping working notes: the shared notes of the team (`Shared/`) and your own (`User/`). |
