# Berries User and Execution Workflow

This document defines application flow, Explorer interaction, portrait operations, Move semantics, Undo, and Execute. Terminology and invariants are defined in `MODEL.md`; discovery and structural ranking are defined in `ANALYSIS.md`.

## Application shell

Berries uses one long-lived application window.

The File menu currently provides:

    Select Roots...
    Load Saved Session...      disabled
    Save Session...            disabled
    Execute...
    Exit

`Select Roots...` returns to the root-selection view. If the selected roots still match the current session, `Explore` returns to that session without rescanning. A changed Corpus requires a new scan.

A persistent status bar reports scan/analysis activity, selection summary, and progress.

## New session flow

Current behavior:

    choose Corpus roots
        -> Explore
        -> Corpus view appears immediately
        -> scan / Group discovery / derived analysis runs
        -> Groups view opens when ScanAsync completes

The initial engine path is currently sequential through Suggestion discovery. The UI architecture does not require this to remain so; derived analysis after portrait operations already refreshes in the background.

## Explorer projections

### Groups

One pane. Each root is one Group; leaves are full-path files in that Group.

### Directory

One exact Directory showing grouped files directly contained there.

### Branch

One Branch showing grouped files organized under its directory hierarchy.

### Corpus Roots

One Branch-style tree for each selected Corpus root.

### Directory Pair

Two exact Directories shown side by side. The title reports shared Group count.

### Branch Pair

Two Branches shown side by side. The title reports shared Group count.

Higher directory nodes are selection shortcuts for represented files; they are not themselves filesystem-operation targets.

## Navigation

### Pivot

Pivot changes the projection/focus without changing the Working Portrait.

Current Pivot choices include:

    Corpus Roots
    Group
    Containing Directory
    Branch
    Best Directory Pair
    Best Branch Pair
    Current Suggested Branch Pair

Availability depends on the current focus and on which derived analysis results are valid.

### Suggest

A Suggestion is a view Berries has identified as worth the user's attention because its structure indicates that one or a few decisions may resolve a relatively large amount of duplicated material.

The currently implemented Suggestions are Branch Pair views. `Suggest` cycles through them and opens the corresponding Branch Pair.

Seed and Counterpart are internal search concepts used to find these Suggestions. They are not exposed as separate user tasks: a Seed is a promising starting Branch, and its highest-scoring Counterpart forms that Seed's best candidate Branch Pair. Several Seeds are compared before the next Suggestion is chosen, so the Suggested Branch Pair often does not come from the highest-ranked Seed.

A Suggestion is not a persistent work item and does not require the user to resolve it.

### Breadcrumbs

Directory and Branch projections expose ancestry within the Corpus. In pair projections each side has its own breadcrumbs, allowing either scope to be broadened or narrowed while preserving the other side.

### Back / Forward

Controls are present but navigation-history behavior is not yet implemented.

## Selection

Selection is persistent across projections and always denotes files in the current Working Portrait.

Selecting a Group or a higher structural node means selecting the represented descendant files.

The status bar reports:

    selected file count
    number of Groups touched by the selection
    selected files outside the current view

### Invert

`Invert Selected Copies` inverts the selection among complete Groups containing at least one selected file.

`Invert All Groups` inverts every represented file in the current Groups projection.

Invert changes selection only.

## Exclude

Exclude removes selected files from the Working Portrait without creating filesystem Actions.

Excluded files disappear from Group membership and all projections. They remain physically present on disk and return if the operation is undone.

Configuration `[exclude]` filtering produces the same logical absence but is applied during initial acquisition rather than recorded as an interactive operation.

## Delete

Delete removes selected files from the Working Portrait and adds deletion Actions.

Berries does not interrupt ordinary portrait editing merely because every currently modeled copy of some content has been selected for deletion. Physical content-loss consequences are summarized immediately before Execute.

## Move

Move is duplicate-motivated structural relocation, not general-purpose file management.

Move is available in pair projections. The left and right scopes explicitly establish the source/destination correspondence.

For a source scope `S`, destination scope `D`, and selected source file `f`:

1. Compute `f`'s relative parent-directory path beneath `S`.
2. Apply that relative directory path beneath `D` to obtain the exact destination Directory.
3. If the same ContentId already exists directly in that destination Directory, retain the existing destination file and reduce the source work to Delete.
4. Otherwise use the source filename in that destination Directory.
5. Free path: Move.
6. Same filename and same ContentId: retain destination, Delete source.
7. Same filename and different content: record a collision; leave source and occupant unchanged; continue the rest of the requested files.

The search for existing identical content is limited to the exact computed destination Directory, not descendants.

Berries never invents a filename and never overwrites different content.

Unique files participate in collision detection even though they are not shown as Group-resolution targets.

## Immediate portrait update

Exclude/Delete/Move update the Working Portrait immediately. There is no separate Apply step.

After a command:

1. the visible projection is rebuilt from the new Working Portrait;
2. persistent selection is rebound to surviving/moved paths;
3. derived Directory/Branch/Suggestion analysis is invalidated;
4. that derived analysis is recomputed in the background;
5. capability state is refreshed when results arrive.

The Explorer remains usable as the stable work surface; background completion does not automatically change the current focus.

## Undo

One user command is one Undo step, even when it affects multiple files.

The session stores ordered portrait operations. Undo removes the latest top-level operation and deterministically rebuilds:

    Working Portrait
    Groups
    filesystem Actions
    selection binding

Undo is separate from navigation history.

## Execute

Execute is the physical commitment boundary.

### Before execution

The GUI asks for explicit approval and reports at least:

    planned filesystem Action count
    Groups with no surviving physical file after the plan

### Execution

Berries attempts the accumulated Actions without a global filesystem rescan/reconciliation pass.

Filesystem failures are handled locally. Independent later work continues when safe.

A Move is first attempted as a filesystem Move. If that raises `IOException`, execution falls back to Copy then Delete. If Copy fails, the source remains.

### After execution

The status reports:

    completed Actions
    skipped dependent Actions
    failures

Failure detail can be displayed separately.

## Save / Load

Save/Load is deliberately unimplemented; both menu items are disabled.

A future saved session should restore the modeled session directly. A user wanting a fresh filesystem view should start a new session and scan again.

## Filesystem abstraction

Core remains independent of Windows-specific path syntax and filesystem APIs.

The abstraction provides the capabilities needed by the current model: enumeration, path hierarchy/navigation, content reads, existence checks, directory creation, copy, move, delete, and directory removal.

Platform-specific behavior belongs in `Berries.FileSystem.Windows`.
