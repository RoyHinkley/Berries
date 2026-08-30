# Berries User and Execution Workflow

This document describes the workflow implemented by the current application. Domain terminology is in `MODEL.md`; analysis mechanics are in `ANALYSIS.md`.

## Application shell

Berries uses one long-lived Avalonia window with two primary surfaces:

    root selection
    Duplicate Explorer

The File menu currently contains:

    Select Roots...
    Load Saved Session...      [disabled]
    Save Session...            [disabled]
    Execute...
    Exit

The root-selection view says "Search these directories for duplicates" and provides:

    Add...
    Remove
    Explore

Selected roots are normalized to a minimal disjoint set.

## Starting or returning to a session

Pressing Explore behaves in two ways:

- if the selected roots match the current session Corpus, return to that session without rescanning;
- otherwise start a new scan/session.

When a new scan starts, the Explorer appears immediately in a Corpus-root view while the scan is running. Status text and a bottom progress bar report acquisition/hashing progress.

The current initial scan does not become fully interactive at the Group level until `BerriesApplication.ScanAsync()` finishes its derived analysis. After completion, the normal starting projection is Groups.

## Explorer projections

The Explorer can Pivot among several views of the same Working Portrait.

### Groups

One pane. Each Group represents one currently duplicated content identity and lists its files by full path.

The underlying Core type is `DuplicateSet`, but the user-facing word is Group.

### Directory

One pane rooted at one exact Directory. It shows duplicate files directly contained there.

### Branch

One pane rooted at one Branch (a directory plus descendants), preserving directory hierarchy beneath the Branch.

### Corpus Roots

One pane containing all selected Corpus roots as Branch-style trees.

### Directory Pair

Two equivalent panes rooted at two exact Directories that share Groups directly.

### Branch Pair

Two equivalent panes rooted at two Branches that share Groups across their descendant trees.

Pair views are the surfaces on which Move left/right is meaningful.

## Pivot and navigation

The current Pivot menu includes:

    Corpus Roots
    Group
    Containing Directory
    Branch
    Best Directory Pair
    Best Branch Pair
    Current Suggested Branch Pair

Structural views use clickable breadcrumbs. In Branch-Pair views, each side has an independent breadcrumb chain, allowing either scope to be broadened/narrowed while retaining the pair relationship.

The shell includes Back and Forward controls, but navigation-history behavior is not yet implemented.

## Suggest

The `Suggest` button cycles through the current targeted Branch-Pair candidates produced by analysis.

A suggestion changes focus/view only. It does not create a required Case, record a decision, or modify the Working Portrait.

The older `Case` terminology remains in some code/research names, but the current interaction is simply: Suggest a promising place to look, then Pivot/navigate/operate normally.

## Selection

Selection persists semantically across projections and ultimately denotes files in the current Working Portrait.

Selecting a Group or structural node is shorthand for selecting its represented descendant files. Files that disappear from the Working Portrait are automatically removed from selection when the session rebuilds.

The status bar reports the current selection summary and provides Clear.

### Invert Selected Copies

For every Group represented by the current selection, select its other current copies and deselect the selected ones. Unrelated Groups are unchanged.

### Invert All Groups

Invert every file in every Group represented in the current projection.

## Portrait operations

The primary commands are:

    Exclude
    Delete
    Move ->
    <- Move
    Undo

There is no user-facing Keep, Accept, Settle, or Apply command.

All portrait operations change the virtual Working Portrait immediately. No physical filesystem change occurs until Execute.

### Exclude

Exclude removes selected files from the Working Portrait and therefore from Groups, projections, selection, and subsequent structural analysis.

Exclude produces no physical filesystem Action. The file remains on disk.

### Delete

Delete removes selected files from the Working Portrait and adds corresponding delete work to the current physical Action list.

Delete does not interrupt the user merely because all current copies of some content have been selected. Potential physical content loss is summarized at Execute.

### Move

Move is available in pair views and uses the pair scopes as source/destination correspondence.

For Move ->, selected files on the left side are mapped beneath the right-side scope. For <- Move, the reverse applies.

For each requested source file:

1. preserve its relative directory path beneath the selected source scope;
2. map that relative directory path beneath the destination scope;
3. if the same content already exists directly in that exact destination directory, keep the destination copy and reduce the source to Delete;
4. otherwise use the source filename;
5. if the destination path is free, move there;
6. if that path contains identical content, keep the destination copy and Delete the source;
7. if that path contains different content, report a collision and leave that source unchanged while continuing other requested sources.

Berries does not invent filenames and does not overwrite different content.

Unique files are not resolution targets but remain modeled so they can cause step 7 collisions.

### Undo

Each top-level user command is one Undo step, even when it contains multiple per-file operations.

Undo removes the most recent portrait operation and deterministically rebuilds:

    Working Portrait
    current Groups
    Action list
    valid selection state

from the fixed Initial Portrait plus the remaining operation history.

## Analysis after portrait operations

After Exclude/Delete/Move/Undo changes the operation history, the current derived structural results are invalidated.

The GUI then:

1. cancels any older analysis refresh;
2. refreshes the current visible projection from the new Working Portrait;
3. restores/synchronizes visible selection;
4. returns the Explorer to use;
5. recomputes Directory analysis, Branch statistics, and Branch counterparts in the background.

The status bar indicates that analysis is updating. When the current background generation completes, capabilities such as Suggest are updated. The analysis does not automatically change the user's current projection.

## Execute

Execute is enabled when the session has physical Actions.

### Pre-execution confirmation

Before disk changes, the application displays:

    planned filesystem Action count
    number of Groups whose content would have no surviving physical file
    notice that independent failures will not stop unrelated safe work

The user must explicitly approve execution.

### Execution behavior

Execution attempts the current Action list against the physical filesystem. It does not first perform a global rescan/reconciliation.

Failures are local outcomes. The executor continues independent safe work where possible and preserves dependency safety. In particular, if a move must be implemented as copy followed by delete, failure of the prerequisite copy suppresses the dependent source deletion.

### Post-execution result

The status bar reports completed, skipped-dependent, and failed counts. When failures exist, the GUI displays a failure summary containing the affected Action and error message.

The current implementation does not automatically create a new post-execution session reflecting whatever subset of filesystem work succeeded; the execution report is the factual result of attempting the approved plan.

## Progress and busy behavior

A persistent bottom status bar owns scan, analysis, portrait-operation, and execution progress.

During a portrait command itself, the Explorer/menu are temporarily disabled while the Working Portrait is rebuilt and the visible projection refreshed. They are re-enabled before the heavier structural reanalysis runs in the background.

This is intentionally different from the initial scan, whose current controller method still awaits all derived analysis before returning.

## Configuration exclusion

`Berries.config` uses `[exclude]`.

Configuration exclusion is applied while acquiring the initial scan. Its user-visible result is the same as never including those files in the working Corpus. It does not create physical Actions.

## Saved sessions

`Save Session...` and `Load Saved Session...` are present but disabled. Persistence behavior is therefore not currently part of executable workflow.

If implemented later, saved-session semantics should be documented from the actual runtime design at that time rather than treated as already settled behavior.

## Deliberately outside the current workflow

The current application does not provide:

    general unique-file maintenance
    arbitrary rename
    automatic semantic Situation classification
    Accept/Settle workflow
    mandatory Case queue
    exhaustive Branch-Pair enumeration
    saved-session persistence
    implemented Back/Forward navigation history

The Explorer remains intentionally centered on duplicate Groups, structural Pivot/navigation, explicit Exclude/Delete/Move operations, Undo, and final Execute.
