# Berries Domain Model

This document defines the current architectural vocabulary and invariants used by Berries. `PROJECT.md` gives the overview; `ANALYSIS.md`, `WORKFLOW.md`, `SITUATIONS.md`, and `BOUNDARY.md` cover specialized topics.

## Public language versus internal types

The user-facing application deliberately uses simpler terminology than parts of Core.

    Group
        the UI term for one currently duplicated content identity
        internal analysis type: DuplicateSet

    file / copy
        one displayed filesystem instance
        internal domain type: FileInstance

    shared Groups
        distinct duplicated content represented on both sides of a structural pair

    Directory
        one exact directory

    Branch
        a directory together with all descendants

    Directory Pair / Branch Pair
        the corresponding two-sided structural views

The internal names `ContentId`, `DuplicateSet`, and `FileInstance` remain precise implementation terms. They should not leak into ordinary UI wording unless needed for developer documentation.

## Core terminology

### Filesystem

The physical storage state observed at scan time and modified only at Execute. The filesystem is larger than the logical Corpus.

### Corpus

The logical material Berries is working with. A new Corpus begins with one or more selected filesystem roots. Roots are normalized so no retained root is a descendant of another retained root.

Configuration exclusion filters material during acquisition. Interactive Exclude removes material from the Working Portrait. Both have the same user-visible meaning: the excluded file is no longer part of the working Corpus and is not physically changed merely by being excluded.

### Corpus root

A selected top-level directory contributing a tree to the Corpus.

### FileInstance

One filesystem instance of bytes at one path. The UI normally calls this a file or copy.

### Content / ContentId

The byte content of a file and the identity Berries assigns to equal bytes. Current duplicate discovery uses SHA-256 after size grouping.

### Group / DuplicateSet

All current Working-Portrait FileInstances having one identical ContentId, provided at least two such instances remain.

`DuplicateSet` is the Core/analysis type. `Group` is the user-facing term.

### Initial Portrait

The fixed modeled scan result for a session after duplicate discovery and expected access evictions have established the usable file population and content identities.

The current implementation retains unique files as well as duplicate files. Unique files are not resolution targets, but they are needed for filesystem constraints such as Move destination collisions.

The Initial Portrait is treated as fixed session truth. Berries does not continuously reconcile it with external filesystem changes.

### Working Portrait

The modeled state of the user's desired Corpus during the session.

It is deterministically rebuilt from the Initial Portrait plus the ordered portrait-operation history. It is not required to match current physical disk state before Execute.

### Portrait operation

An undoable virtual transformation of the Working Portrait. Current operations are:

    Exclude
    Delete
    Move

One top-level user command is stored as one Undo step, potentially as a batch of per-file operations.

### Exclude

Remove selected files from the Working Portrait without producing physical filesystem Actions.

Excluded files disappear from Groups, structural projections, selection, and derived analysis. Undo can restore them.

### Delete

Remove selected files from the Working Portrait and add corresponding physical delete work to the Action list.

### Move

Relocate selected duplicate files virtually from an explicit source scope to an explicit destination scope while preserving source-relative structure.

For each source file, compute the exact destination directory from its relative path beneath the selected source scope.

1. If identical content already exists directly in that computed destination directory, preserve the destination copy and reduce the source operation to Delete.
2. Otherwise use the source filename in the computed destination directory.
3. If that path is free, Move there.
4. If that path already contains identical content, preserve the destination copy and Delete the source.
5. If that path contains different content, report a collision and leave that source unchanged while continuing other requested files.

Unique destination files participate in collision detection. Berries does not invent filenames or overwrite different content.

### Selection

A persistent set of Working-Portrait file paths shared across projections.

Every visible selection resolves to files. Selecting a Group or structural node is shorthand for selecting its applicable descendant files.

The current UI provides two inversion behaviors:

    Invert Selected Copies
        for Groups represented in the current selection, swap selected and
        unselected copies of those same Groups

    Invert All Groups
        invert every file in every Group represented in the current view

### Directory

For direct-directory statistics, one exact directory. Descendant files are not folded into its direct counts.

### Branch

A directory together with all descendants.

### DirectoryPair

An unordered pair of exact directories sharing one or more duplicated content identities directly. The UI term is Directory Pair.

### BranchPair

A pair of non-nested Branches sharing duplicated content. The UI term is Branch Pair.

The current analysis does not comprehensively enumerate every possible BranchPair. It searches targeted promising seeds and can also find the best counterpart for a selected Branch on demand.

### Projection

A UI-independent organization of Working-Portrait duplicate files. Current projections are:

    Groups
    Directory
    Branch
    Directory Pair
    Branch Pair
    Corpus Roots

Projection construction lives in `Berries.Projection`, separate from Avalonia presentation.

### Pivot

A navigation command that changes the current projection or scope without changing the Working Portrait.

### Suggest

A UI command that presents a promising analyzed Branch Pair. Suggestions are attention aids, not required work items or persistent Cases.

The code and older research still contain `Case` terminology in places, but the current user interaction is projection- and operation-oriented.

### Situation

Historical/optional semantic vocabulary describing why duplication may exist, such as backup, migration, archive, or reorganization.

Situation classification is not required by the current Explorer and does not gate Exclude/Delete/Move. `SITUATIONS.md` preserves that research separately.

### Action

A primitive physical filesystem operation used at Execute. Current planning/execution types include delete, move, and copy where required by execution mechanics.

### Action list / plan

The physical work accumulated from the current portrait-operation history. It is derivative of the desired Working Portrait.

Exclude contributes no physical Action. Delete and Move do.

### Undo

Remove the most recent top-level portrait operation and rebuild the Working Portrait, Groups, selection validity, and Action list from the fixed Initial Portrait plus remaining operations.

### Execute

The explicit physical commitment boundary.

Before Execute, Berries summarizes planned Actions and counts content identities that would have no surviving physical file after the plan. After approval, it attempts the Action list against current disk state.

There is no global pre-execution rescan/reconciliation. Failures are handled locally. Independent safe work continues. Dependent destructive work is suppressed if its prerequisite fails.

### Saved session

Save/Load commands exist in the shell but are currently disabled. No persistence schema is authoritative yet.

## Current analysis state

`BerriesApplication` owns the currently published derived results:

    DirectoryAnalysis
    BranchStatistics
    Counterparts

Any portrait-changing command invalidates all three by clearing them. The GUI refreshes the current visible projection immediately, then starts a cancellable background recomputation of the derived analysis.

Initial scan is currently more linear: `ScanAsync()` establishes the session and then awaits the full derived-analysis refresh before returning.

## Transitional internal terminology

`DuplicateSettlements` and some `Case`-oriented types remain in Core from earlier experimental designs. They are not user-facing concepts in the current application.

The active application constructs a new empty `DuplicateSettlements` object only as a compatibility input to older analyzer signatures; no UI operation records accepted/settled relationships. Consequently it has no semantic effect on current Explorer behavior.

Developer documentation should describe these as transitional internals rather than reviving Accept/Settle as product concepts.

## Governing invariants

1. The Corpus is logical working scope, not the whole filesystem.
2. The Initial Portrait is fixed for a session.
3. The Working Portrait is reconstructible from the Initial Portrait plus ordered portrait operations.
4. Groups are derived from equal known Content in the current Working Portrait.
5. Selection ultimately denotes files, regardless of projection node type.
6. Exclude changes only virtual working scope.
7. Delete and Move change the Working Portrait immediately and contribute physical Actions.
8. There is no user-facing Keep, Accept, Settle, or Apply step.
9. Pivot and Suggest change attention/view, not filesystem intent.
10. Unique files remain modeled even though duplicate-resolution commands do not target them.
11. No physical filesystem change occurs before Execute.
12. Execute attempts the approved plan and reports encountered results rather than requiring a global revalidation pass.
13. Core remains independent of Avalonia and Windows-specific filesystem behavior.
