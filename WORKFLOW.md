# Berries User and Execution Workflow

This document defines application flow, Duplicate Explorer interaction, portrait operations, Move semantics, execution, and persistence direction. Terminology and invariants are defined in `MODEL.md`; discovery/ranking is defined in `ANALYSIS.md`.

## Application shell and program flow

Berries should use one long-lived application shell rather than a wizard sequence of analysis screens.

A conventional `File` menu should provide the application-level commands:

    Select Roots...
    Load Saved Session...      [may initially be disabled]
    Save Session...            [may initially be disabled]
    Execute...
    Exit

### New session

`Select Roots...` shows the root-selection view. The current Add / Remove / Scan interaction is appropriate.

Selected roots are normalized to a minimal disjoint set. Scan begins a new session and transitions into the Duplicate Explorer as soon as practical. The corpus roots themselves provide the initial Explorer structure while analysis results become available.

There is no special pre-analysis exclusion/settlement screen.

### Loaded session

`Load Saved Session...`, when implemented, restores the saved session directly into the Duplicate Explorer. It does not rescan or reconcile the filesystem.

### Long-lived Explorer

The Duplicate Explorer is the primary working UI. Analysis should progressively enrich it rather than requiring a completed monolithic scan before the application can become interactive.

Heavy work should migrate toward asynchronous background tasks where practical. The architecture should not assume that every analysis stage must block the UI or that all derived analysis is simultaneously current.

A status bar at the bottom of the main window owns progress reporting. It should show the current stage and meaningful progress within that stage, for example hashing count/percentage. Avoid modal progress UI.

Controls such as `Suggest Case` become enabled when the analysis required to support them is available. Background recomputation must not steal focus or unexpectedly restructure the user's current Explorer view.

## Explorer projections

The Explorer presents the Working Portrait through interchangeable projections.

### Content projection

One pane. Each root node represents one duplicated Content; leaves are the full-path FileInstances having that Content.

All FileInstances are shown individually. Do not substitute directories as leaves: multiple identical instances can exist in one directory, and operational scope must remain instance-precise.

Selecting a Content node is shorthand for selecting its currently represented FileInstance descendants.

### DirectoryPair projection

Two equivalent tree panes rooted at two exact directories. It is a narrow container-centric view and can be understood as a degenerate BranchPair projection.

### BranchPair projection

Two equivalent tree panes rooted at two branches. Directory nodes organize duplicate FileInstances. Selecting a directory node selects the applicable duplicate FileInstances beneath it; the directory itself is not an operation target.

Unique files are not shown as resolution candidates, although the session can retain knowledge of them for collision detection and other constraints.

## Navigation and Pivot

Cases are suggested foci, not a queue and not a separate mandatory Case screen.

Berries should provide a command such as `Suggest Case` whose purpose is:

    identify something likely to reward attention

A suggestion establishes an appropriate focus/projection. The user may then navigate or Pivot freely.

`Pivot` changes the projection or structural interpretation of the current focus without modifying the Working Portrait. It belongs with navigation controls, along with Back/Forward, rather than with Undoable resolution operations.

Exact Pivot availability is context-sensitive and can evolve with the Explorer implementation.

## Selection model

Selection always reduces to a set of duplicate FileInstances in the Working Portrait.

    leaf selection
        one FileInstance

    Content selection
        all represented instances of that Content

    directory/branch node selection
        all applicable duplicate FileInstances represented beneath that node

Ctrl/Shift/multiple selection should behave conventionally where the tree control permits it.

`Invert Selection` is Content-relative. For each Content represented by the current selection, select its other currently selectable instances and deselect the selected instances. Do not invert unrelated visible Contents. Excluded instances are not selectable and therefore cannot be selected by inversion.

## Resolution controls

The primary interaction is selection-first:

    select FileInstances / structural scope
        -> invoke operation
        -> Working Portrait changes immediately

Do not use tool-first paint semantics for destructive operations.

A compact toolbar should expose the principal commands. Context menus may duplicate common commands as a convenience, but should not be the sole discoverability mechanism. Per-item Keep/Delete buttons would create unnecessary clutter and make bulk work awkward.

The working command set is approximately:

    Back / Forward
    Pivot
    Suggest Case
    Invert Selection
    Exclude
    Delete
    Move ->
    <- Move
    Undo

Commands are enabled according to projection and selection validity.

There is no `Keep`. A surviving instance simply has no destructive operation applied to it.

There is no `Accept` or `Settle`. If instances should cease to participate in Berries, `Exclude` removes them from the Working Portrait.

There is no `Apply`. Exclude/Delete/Move immediately update the Working Portrait and operation history. Undo provides local safety; Execute is the ultimate physical commitment boundary.

## Exclude

Exclude removes selected FileInstances from the working Corpus.

The excluded instances disappear from all projections and derived analysis and are no longer selectable. No filesystem Action is produced.

Record Exclude as an undoable portrait operation, conceptually one excluded instance per operation/history entry even if a bulk UI command groups them for Undo presentation.

Configuration exclusions have the same semantic result. `Berries.config` should use `[exclude]`, not `[ignore]`. Configuration exclusions are automatic initial Exclude operations; implementation may filter them during acquisition for efficiency.

## Delete

Delete removes selected FileInstances from the Working Portrait and contributes deletion work to the eventual Action Plan.

Do not interrupt ordinary editing merely because the operation removes every remaining instance of one or more Contents. Potential Content loss is summarized at the pre-execution review, not treated as an early warning.

## Move

Move is duplicate-motivated structural relocation, not a general file-management command and not an inferred directory merge.

The user explicitly selects source and destination scopes in a two-pane projection. The selected scopes establish the directory correspondence. Berries preserves source paths beneath the selected source scope when mapping them beneath the destination scope.

Example:

    source scope:       Old\Photos\Trips
    destination scope: Photos\Travel

    source instance:   Old\Photos\Trips\2024\a.jpg
    computed directory:Photos\Travel\2024

The duplicate counterpart's existing directory elsewhere does not establish a mapping. If the user later wants a broader mapping, it is a separate Move operation. Thus a reorganization can naturally be expressed by a sequence such as:

    Old\Photos\Trips -> Photos\Travel
    Old\Photos       -> Photos

The first handles the renamed/reorganized subtree; the second handles the remainder.

### Destination-authoritative collision semantics

Choosing the destination asserts that its existing organization is the right place. Work already completed there is authoritative. Berries does not rename destination files, invent filenames, or undo existing organization.

For each selected source FileInstance:

1. Determine the exact computed destination directory from the source's relative directory path.
2. If the same Content already exists **within that exact destination directory**, regardless of filename, the desired Content is already in the right place. Retain the existing destination name and reduce the source operation to Delete.
3. Otherwise compute the destination path using the source filename.
4. If that path is free, Move the source there.
5. If that path contains identical Content, retain the destination and Delete the source.
6. If that path contains different Content, flag the collision immediately, leave source and destination untouched, and continue with the rest of the requested Move.

`Within` means directly in the computed destination directory, not somewhere beneath its descendant tree.

Unique destination files participate in collision detection even though unique files are not selectable resolution targets. The Initial/session model therefore retains enough filesystem knowledge to detect them.

There is no separate Merge command. Move plus the destination-authoritative collision rules covers the duplicate-resolution behavior we need. General rename and general unique-file reorganization remain out of scope.

## Undo and navigation history

Undo reverses portrait operations. It does not mean Back.

Back/Forward reverse navigation/Pivot history. Exclude/Delete/Move belong to the operation history; Pivot and ordinary navigation belong to view history.

The exact grouping of a multi-instance command into one user-visible Undo step is a UI implementation detail, but the model must retain enough information to reconstruct the Working Portrait deterministically.

## Derived analysis after portrait changes

The Working Portrait changes immediately after Exclude/Delete/Move. Duplicate membership and higher-level evidence derived from it can therefore change.

Correct incremental invalidation is desirable, but broad in-memory recomputation is acceptable initially if measured performance is adequate. Known Content need not be reread or rehashed merely because a virtual operation changed its location or membership.

Asynchronous recomputation should publish availability/results without forcing the user out of the current view. A suggestion can become stale and be recomputed; the Explorer itself remains the stable interaction surface.

## Save / Resume

Persistence is conceptually straightforward but deliberately optional for the next implementation revision.

A saved session should serialize one session object containing all state necessary to resume directly, including at least:

    Initial Portrait
    ordered portrait-operation history and/or directly reconstructible Working Portrait
    selected Corpus roots and exclusion state
    Content identities and analysis state worth retaining
    useful Explorer/view state

JSON is preferred for inspectability and schema evolution. Session files should normally be compressed because Portrait data can be large.

`Save Session...` and `Load Saved Session...` may be present but nonfunctional/disabled until actual use demonstrates that persistence is worth implementing. Avoid freezing a persistence schema while the runtime model is still evolving.

There is no saved-session reconciliation workflow. To obtain a fresh filesystem view, the user starts a New Session by selecting roots and scanning.

## Execute

Execute is application-level and deliberately separated from ordinary Explorer operations.

### Pre-execution summary

Before modifying disk, show an execution summary describing the intended work and ask for explicit approval. Include at least:

    planned moves/copies/deletions
    Content that will have no surviving instance in the final working Corpus
    other useful aggregate consequences

This is the appropriate place to call attention to Content loss. Ordinary Delete editing does not raise a last-copy warning.

### Execution practice

After approval, assume the Action Plan is valid and attempt it. Do not globally validate/rescan/reconcile the filesystem first. Such a pass cannot guarantee later atomic operations will succeed and introduces an unnecessary time-of-check/time-of-use race.

Filesystem operations can fail. Handle failures locally and preserve dependency safety:

    independent operations continue where safe;
    a failed prerequisite suppresses dependent destructive work;
    a cross-device Move may be implemented as Copy followed by Delete;
    if that Copy fails, the source Delete must not occur;
    if an ordinary Move fails, leave the source intact;
    record failures/exceptions for the post-execution report.

Move collisions known from the Working Portrait are handled when Move is requested. New conflicts or changes that arise later are ordinary execution failures.

### Post-execution summary

After execution, report what actually happened:

    successful operations
    failures and exceptions
    skipped dependent operations
    conflicts/discrepancies encountered during execution

Do not repeat Content-loss warnings as warnings here; those were part of the approved pre-execution plan. The post-execution report is factual.

## Filesystem abstraction requirements

Core must remain independent of Windows path syntax, drive letters, NTFS IDs, ACLs, reparse points, case-insensitive behavior, and other platform assumptions.

The abstraction should provide the least capabilities required by the model: hierarchy/navigation, metadata needed for discovery, readable ordinary Content, and safe create/copy/move/delete operations.

Symbolic/special objects remain outside the initial ordinary-file model. Hard links need no special treatment initially if the adapter exposes their directory entries as ordinary instances. Duplicate identity is based on ordinary byte Content, not ACLs, alternate streams, ownership, or other metadata.

## Deliberately deferred

The following are not required before the next Explorer implementation:

    Save/Load implementation
    general unique-file maintenance
    rename as a user operation
    sophisticated Situation inference
    persistent learned rules
    exhaustive BranchPair generation
    aggressive incremental/background recomputation optimization
    exact final toolbar styling and projection-specific visual polish

The architecture should leave room for these without implementing them prematurely.
