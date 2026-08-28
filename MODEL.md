# Berries Domain Model

This document defines the architectural vocabulary and semantic invariants used throughout Berries. `PROJECT.md` is the governing overview; `ANALYSIS.md`, `SITUATIONS.md`, and `WORKFLOW.md` contain the corresponding detailed designs.

## Core terminology

### Filesystem

The physical universe from which Berries observes and eventually modifies files. The filesystem is larger than the Corpus: objects outside selected roots, and objects excluded from Berries, remain ordinary filesystem objects without belonging to the working Corpus.

### Corpus

The logical set of filesystem material the user intends Berries to work with.

A new Corpus begins with one or more selected filesystem roots. Roots are normalized so no retained root is a descendant of another retained root. Exclusion subtracts material from the Corpus.

The Corpus is not a synonym for the filesystem and need not contain every object beneath a physical volume or filesystem hierarchy.

### Corpus root

A filesystem directory selected to contribute its tree to the Corpus.

### FileInstance

One filesystem instance of Content at a particular path. A FileInstance is an instance/location, not the byte content itself.

### Content

The byte sequence contained by a FileInstance. Content identity is established sufficiently for Berries by duplicate detection; the current implementation uses SHA-256 after size grouping.

### Initial Portrait

The modeled state observed when a new session scans its selected roots. It is the fixed starting point from which the Working Portrait can always be reconstructed.

Configuration exclusions are semantically initial Exclude operations even if the implementation optimizes them by filtering during acquisition.

### Working Portrait

The modeled state of the user's desired Corpus during the session.

It is deterministically obtained by applying the ordered sequence of portrait operations to the Initial Portrait. The principal portrait-changing operations are:

    Exclude
    Delete
    Move

The Working Portrait is not required to match the current physical filesystem before Execute. Until Execute, it is a virtual design.

### Portrait operation

An undoable operation that transforms the Working Portrait.

Portrait operations and filesystem Actions are related but distinct:

    Exclude
        removes selected FileInstances from the working Corpus;
        produces no filesystem Action

    Delete
        removes selected FileInstances from the Working Portrait;
        produces filesystem deletion Actions

    Move
        relocates selected duplicate FileInstances in the Working Portrait;
        produces the filesystem Actions necessary to realize that relocation

The ordered portrait-operation history is sufficient to reconstruct the Working Portrait from the Initial Portrait.

### Exclude

Remove selected FileInstances from the working Corpus without changing the filesystem.

An excluded instance:

    is absent from the Working Portrait;
    does not participate in duplicate analysis, projections, Cases, or suggestions;
    is not selectable, including through Invert Selection;
    produces no filesystem Action;
    remains physically present on disk;
    can be restored by Undo.

Exclusion is portrait state, not an assertion that duplication is acceptable. It replaces the earlier settlement/acceptance concept.

### Duplicate

A FileInstance whose Content is identical to at least one other FileInstance in the same Working Portrait.

### DuplicateSet

All FileInstances in the Working Portrait having one identical Content identity, when at least two such instances exist. One DuplicateSet represents exactly one distinct duplicated Content.

### Directory

For duplicate-analysis statistics, a Directory record describes Files directly contained by that directory only. Descendants are not folded into local counts.

### DirectoryPair

An unordered pair of distinct directories having one or more distinct duplicated Contents directly represented in both directories. DirectoryPair is useful derived evidence and a useful narrow projection, but need not be a separately generated first-class Case type.

### Branch

A directory together with all descendants. `Branch` is the formal tree-structural term; `scope` remains useful in ordinary discussion for any chosen bounded region.

### BranchPair

A pair of directory-rooted Branches whose descendants exhibit duplicate-content relationships. BranchPair is the principal container-centric Case/focus.

Comprehensive BranchPair enumeration is no longer required by the design. Promising BranchPairs can be discovered by selecting promising Branch seeds and searching for strong counterparts.

### Case

An objectively derived unresolved duplication question or promising focus in the Working Portrait.

Cases are suggestions for attention, not persistent workflow objects and not a mandatory queue. Berries should identify something likely to reward attention; the user remains free to navigate and Pivot elsewhere.

The three useful duplication viewpoints currently recognized are:

    Content / DuplicateSet
        Why is this Content duplicated in these locations?

    same-directory
        Why are multiple instances of this Content present in one directory?

    BranchPair / container-centric
        Why is so much duplicated Content represented between these branches?

DirectoryPair can be treated as a narrow/degenerate BranchPair projection.

### Situation

Optional semantic context explaining a Case. Situation remains useful vocabulary and research material, but the Explorer and resolution operations do not require the user to classify a Situation before acting.

### Projection

A way of organizing the duplicate FileInstances of the Working Portrait for exploration and resolution.

Initial projections are:

    Content
        one pane; Content nodes with full-path FileInstance leaves

    DirectoryPair
        two equivalent panes rooted at two exact directories

    BranchPair
        two equivalent panes rooted at two branches

Projection changes presentation, not resolution semantics.

### Pivot

A navigation operation that changes projection and/or focus without changing the Working Portrait.

Pivot belongs with navigation, not Undoable portrait operations. Back/Forward navigation is the natural inverse mechanism for navigation history.

### Selection

A set of duplicate FileInstances in the current Working Portrait.

This is the common operational abstraction across projections. Selecting a leaf selects one FileInstance. Selecting a higher tree node is shorthand for selecting the applicable duplicate FileInstances represented beneath that node.

Directories are not themselves filesystem-operation targets in the current design. Higher-level nodes define resolution scope.

### Invert Selection

A selection operation, not a portrait operation. For every Content represented in the current selection, select the other currently selectable instances of that Content and deselect the selected ones.

Excluded instances are absent from the Working Portrait and therefore never participate in inversion.

### Resolution operation

A positive operation applied to selected duplicate FileInstances. The current set is deliberately small:

    Exclude
    Delete
    Move left-to-right / right-to-left

There is no Keep operation. Survival is simply the absence of a destructive operation.

There is no Accept/Settle operation. If duplication should no longer participate in Berries, Exclude expresses that directly by removing the relevant instances from the working Corpus.

There is no Apply step. Resolution operations immediately update the Working Portrait and operation history. Undo provides local reversibility; Execute is the separate filesystem commitment boundary.

### Move

A duplicate-motivated relocation from a selected source scope to a selected destination scope.

For each selected source FileInstance, preserve its relative path beneath the selected source scope when mapping it beneath the destination scope. Directory correspondence is explicitly established by the scopes the user selects; Berries does not infer that one differently named source directory maps to another differently named destination directory merely because their files duplicate each other.

For each source instance, let the computed destination directory be the exact directory obtained by applying the relative source directory path beneath the destination scope:

1. If identical Content already exists **within that exact destination directory**, regardless of filename, the destination is authoritative and the source becomes a deletion. Existing destination naming is preserved.
2. Otherwise, if the computed destination filename is free, move the source there.
3. Otherwise, if that filename is occupied by identical Content, retain the destination instance and delete the source.
4. Otherwise, the filename is occupied by different Content: flag the collision immediately, leave both files untouched, and continue the rest of the Move.

The collision check includes unique destination files even though unique files are not resolution candidates. Berries retains enough knowledge of the scanned filesystem to detect such constraints.

Move never invents a filename and never overwrites different Content. Rename is outside the current problem scope.

### Action

A primitive physical filesystem operation used to realize portrait operations at Execute time. Typical primitives are copy, move/rename, delete, and required directory creation/removal helpers.

### Action Plan

The filesystem work implied by the current ordered portrait operations. It is derivative of the desired Working Portrait; it is not the definition of that Portrait.

Exclude produces no filesystem Action. Delete and Move do.

### Execute

The explicit commitment boundary at which Berries attempts to realize the approved Action Plan against the physical filesystem.

Berries assumes the Action Plan was valid when constructed. It does not globally rescan or reconcile the filesystem immediately before execution. Any atomic filesystem operation can fail; failures are handled as execution outcomes.

Dependent destructive operations are conditional on prerequisite success. In particular, a cross-device move implemented as copy followed by delete must omit the delete if the copy fails.

Independent operations continue when safe after another operation fails.

### Execution reports

Before Execute, Berries presents a summary of the intended physical work and asks for confirmation. This pre-execution report includes Content that will have no surviving instance in the final working Corpus.

Berries does not raise an earlier warning merely because a Delete operation removes all remaining instances of a Content.

After execution, Berries presents what actually happened: completed operations, failures, skipped dependent operations, exceptions, and other execution discrepancies. Content-loss warnings are not repeated as warnings after execution; by then they are historical consequences of the approved plan.

### Save / saved session

A saved session, if implemented, persists enough state to load the session directly rather than rescanning/reconciling the filesystem. Conceptually this is one serializable session object containing the Initial Portrait, portrait-operation history/Working Portrait state, and useful application state.

The intended representation is JSON, normally compressed because Portraits can be large. Save/Load may remain unimplemented until experience demonstrates that persistence is useful.

Loading a saved session restores that session as-is. Starting fresh means selecting roots and scanning a New Session; there is no required rescan/reconcile workflow for an old session.

## Governing invariants

1. The Corpus is the logical material the user intends Berries to work with; it is not the filesystem.
2. The Initial Portrait is the fixed scanned starting state for a session.
3. The Working Portrait always represents the user's desired Corpus and is reconstructible from the Initial Portrait plus an ordered sequence of portrait operations.
4. Selection always denotes duplicate FileInstances, even when the user selects higher structural nodes.
5. Exclude changes the Corpus/Portrait only; it never changes the filesystem.
6. Delete and Move change the Working Portrait immediately and contribute physical work to Execute.
7. There is no Keep, Accept, Settle, or Apply semantic layer in the current design.
8. Pivot/navigation changes presentation only.
9. Unique files can remain known to the model even though duplicate resolution operations do not target them.
10. No real filesystem change occurs before Execute.
11. Execute attempts the approved plan and handles encountered failures rather than requiring a global filesystem revalidation pass.
12. Core remains independent of UI and platform-specific filesystem behavior.
