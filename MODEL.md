# Berries Domain Model

This document defines the architectural vocabulary and semantic invariants used throughout Berries. `PROJECT.md` is the governing overview; `ANALYSIS.md` and `WORKFLOW.md` contain the corresponding detailed designs.

## Vocabulary layers

Berries intentionally keeps user-facing language simple while retaining narrower technical names where they carry real information.

User-facing nouns are **Group**, **file/copy**, **Directory**, **Branch**, **Directory Pair**, **Branch Pair**, **Corpus Roots**, and **Suggestion**.

Core uses narrower technical types and search-role names where they remain useful:

- `FileInstance` — one filesystem instance at one path.
- `ContentId` — the established identity of a byte sequence. The current implementation derives it from SHA-256 after size grouping.
- **Seed** — a Branch chosen as a promising starting point for targeted Branch Pair search.
- **Counterpart** — a Branch scored relative to a particular Seed; the highest-scoring Counterpart forms that Seed's best candidate Branch Pair.

A **Group** is not synonymous with a `ContentId`: it is the current collection of at least two files in the Working Portrait having the same `ContentId`.

Seed and Counterpart are internal search roles, not alternate names for Branches or Branch Pairs. A **Suggestion** is the application-level result surfaced because a view appears worth the user's attention.

## Core terminology

### Filesystem

The physical universe from which Berries observes and eventually modifies files. The filesystem is larger than the Corpus.

### Corpus

The logical filesystem material the user intends Berries to consider.

A new Corpus begins with one or more selected filesystem roots. Roots are normalized so no retained root is a descendant of another retained root. Configuration and interactive Exclude subtract material from Berries consideration.

### Corpus root

A selected filesystem Directory contributing its tree to the Corpus.

### FileInstance

One filesystem instance of content at a particular path. It carries path, parent Directory, size, observed timestamp, and—when established—`ContentId`.

The Explorer normally calls this simply a file or copy.

### ContentId

Internal identity for byte-identical file content. A ContentId is content identity, not a location and not a user-visible unit of work.

### Group

All files in the current Working Portrait having one ContentId, when at least two such files remain.

A Group therefore represents one distinct duplicated content identity at the current portrait generation. If an operation leaves fewer than two files with that ContentId, that Group naturally disappears.

### Initial Portrait

The modeled state established by scanning the selected roots and completing Group discovery for a new session.

The Initial Portrait is fixed for that session. Berries intentionally does not continuously reconcile it with external filesystem changes; physical discrepancies are encountered at Execute.

Configuration exclusions are applied during acquisition for efficiency and therefore do not appear as later interactive operations.

### Working Portrait

The modeled state of the user's desired Corpus during the session.

It is deterministically obtained by applying the ordered portrait-operation history to the Initial Portrait. The current portrait-changing operations are:

    Exclude
    Delete
    Move

The Working Portrait is virtual and need not match the current physical filesystem before Execute.

### Portrait operation

An undoable user command that transforms the Working Portrait.

    Exclude
        removes selected files from the Working Portrait;
        produces no filesystem Action

    Delete
        removes selected files from the Working Portrait;
        produces deletion Actions

    Move
        relocates selected files in the Working Portrait;
        produces the filesystem Actions necessary to realize that relocation

A multi-file command is stored as one top-level operation batch and therefore one Undo step.

### Exclude

Remove selected files from Berries consideration without changing the filesystem.

An excluded file:

- is absent from the Working Portrait;
- no longer participates in Groups or derived analysis;
- is no longer selectable;
- produces no filesystem Action;
- remains physically present on disk;
- can be restored by Undo.

There is no parallel acceptance state. If material should cease to participate in Berries, Exclude expresses that directly.

### Delete

Remove selected files from the Working Portrait and add physical deletion work to the current Action list.

Potential loss of all physical copies in a Group is summarized immediately before Execute rather than interrupting ordinary portrait editing.

### Directory

One exact filesystem Directory.

For Directory analysis, counts describe files directly contained by that Directory only. Descendants are not folded into local Directory statistics.

### Directory Pair

An unordered pair of exact Directories sharing one or more Groups directly.

`SharedGroupCount` is the number of distinct Groups represented directly in both Directories.

### Branch

A Directory together with all descendants.

Branch statistics aggregate ordinary files, grouped files, Groups, and grouped Directories through ancestry.

### Seed

A Branch chosen as a starting point for targeted Branch relationship search because its Group concentration makes it worth testing against other Branches.

Seed rank answers only where to look first. It does not measure the quality of any resulting Branch Pair.

### Counterpart

A non-nested Branch evaluated relative to one Seed.

Counterparts are ranked by the strength of their relationship with that Seed. The highest-scoring Counterpart forms that Seed's strongest candidate Branch Pair.

### Branch Pair

Two non-nested Branches sharing Groups somewhere beneath them.

A Branch Pair is simply the pair itself. It need not have been discovered through the Seed/Counterpart search; the Explorer can also request the best Branch Pair for an explicitly selected Branch.

Berries does not construct every possible Branch Pair. The targeted search evaluates several strong Seeds and their Counterparts, then compares the resulting pairs.

### Suggestion

A view Berries has identified as worth the user's attention because its structure indicates that one or a few user decisions may resolve a relatively large amount of duplicated material.

The currently implemented Suggestions are Branch Pair views. During targeted search Berries evaluates several candidate Seeds, finds each Seed's best Counterpart, and chooses the strongest Branch Pair among those candidates as the next Suggestion. The winning Suggestion therefore often does not originate from the highest-ranked Seed.

A Suggestion is presentation/navigation state, not a required work item and not a semantic diagnosis.

### Corpus Roots projection

A Branch-style Explorer view rooted at each selected Corpus root.

### Projection

A way of organizing files from the Working Portrait for exploration and resolution.

Current projections are:

    Groups
    Directory
    Branch
    Corpus Roots
    Directory Pair
    Branch Pair

Changing projection does not change the Working Portrait.

### Pivot

Navigate from the current focus to another meaningful projection or structural interpretation without changing the Working Portrait.

### Suggest

Navigate to the next available Suggestion.

The current implementation cycles through Branch Pair Suggestions found by targeted Seed/Counterpart analysis. Suggest is navigation, not an operation and not a required queue.

### Selection

A persistent set of files in the current Working Portrait.

Selection is independent of projection. Higher Explorer nodes are shorthand for the files they represent beneath them.

### Invert

Selection transformation.

`Invert Selected Copies` inverts selection among the complete Groups containing currently selected files. `Invert All Groups` inverts every represented file in the current Groups projection.

Invert does not change the Working Portrait.

### Move

Duplicate-motivated relocation from an explicitly selected source scope to an explicitly selected destination scope.

For each selected source file, preserve its relative directory path beneath the selected source scope when mapping it beneath the destination scope.

For the exact computed destination Directory:

1. If the same ContentId already exists directly there, retain that destination file and reduce the source work to Delete.
2. Otherwise compute the destination path using the source filename.
3. Free path: Move.
4. Same filename and same ContentId: retain destination, Delete source.
5. Same filename and different content: report collision, leave both unchanged, continue other requested files.

Move does not invent filenames and does not overwrite different content.

Unique files remain in the Portrait because they can occupy a destination path even though they are not Group-resolution targets.

### Action

A primitive physical filesystem operation used to realize the Working Portrait at Execute time. Current primitives include Delete, Move, Copy, and necessary directory creation.

### Action list

The physical work implied by the current ordered portrait operations.

Exclude produces no Action. Delete and Move do.

The current implementation stores this directly on `BerriesSession` as `Actions`; it is deterministically rebuilt with the Working Portrait during Undo.

### Execute

The explicit physical commitment boundary.

Berries does not globally rescan or reconcile the filesystem immediately before execution. It attempts the approved Actions and handles encountered filesystem failures locally.

For cross-device movement, execution may fall back from Move to Copy then Delete. The source is not deleted if the Copy fails.

Independent later work continues when safe.

### Execution summaries

Before Execute, Berries reports planned action count and the number of Groups for which the current plan would leave no surviving physical file.

After execution, Berries reports completed Actions, skipped dependent work, and failures.

### Saved session

Save/Load is not implemented. The GUI currently shows disabled commands.

If implemented, saved state should restore a modeled session directly rather than silently reconciling it with the current filesystem.

## Governing invariants

1. The Corpus is logical material Berries is considering; it is not the filesystem.
2. The Initial Portrait is fixed for the lifetime of a session.
3. The Working Portrait is reconstructible from the Initial Portrait plus ordered portrait operations.
4. A Group is the current set of at least two files sharing one ContentId.
5. Selection always denotes files, regardless of projection.
6. Exclude changes the Working Portrait but never the filesystem Action list.
7. Delete and Move change the Working Portrait immediately and contribute physical Actions.
8. There is no Keep, Accept, or Apply state in the current model.
9. Seed and Counterpart are search roles; Suggestion is the surfaced attention unit.
10. Pivot and Suggest change focus/presentation only.
11. Unique files can remain modeled even though Group-oriented resolution does not target them.
12. No physical filesystem change occurs before Execute.
13. Execute handles encountered failures rather than depending on a global pre-execution reconciliation pass.
14. Core remains independent of UI and platform-specific filesystem behavior.
