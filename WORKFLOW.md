# Berries User and Execution Workflow

This document defines application flow, Explorer interaction, portrait operations, Move semantics, Undo, and Execute. Terminology and invariants are defined in `MODEL.md`; discovery and ranking are defined in `ANALYSIS.md`.

## Interaction model

Berries uses a long-lived **Explorer**, not a wizard.

Suggestions reduce the effort required to find useful duplication, but they do not prescribe a sequence of required Cases. A user may open a Suggestion, recognize the relationship immediately, adjust either scope with breadcrumbs, Pivot to another structural view, inspect a Group or Directory, or follow any other useful clue before acting.

The objective is minimum practical user effort, not minimum number of screens or strict adherence to an algorithmic presentation order.

## Application shell

The File menu currently provides:

    Select Roots...
    Load Saved Session...      disabled
    Save Session...            disabled
    Execute...
    Exit

A persistent status bar reports scan/analysis activity, selection summary, and progress.

## New session flow

    choose Corpus roots
        -> Explore
        -> Corpus view appears immediately
        -> scan / Group discovery / derived analysis runs
        -> Groups view opens when ScanAsync completes

The initial engine path is currently sequential through Suggestion discovery. Derived analysis after portrait operations already refreshes in the background.

## Explorer projections

Current projections are:

    Groups
    Directory
    Branch
    Corpus Roots
    Directory Pair
    Branch Pair

A Projection is presentation/navigation state, not a Case.

### Groups

One pane. Each root is one Group; leaves are full-path files in that Group.

### Directory

One exact Directory showing grouped files directly contained there.

### Branch

One Branch showing grouped files organized under its directory hierarchy.

### Corpus Roots

One Branch-style tree for each selected Corpus root.

### Directory Pair / Branch Pair

Two scopes shown side by side. Higher directory nodes are selection shortcuts for represented files, not filesystem-operation targets.

## Cases and Suggestions

A Case is an objective bounded set of current-Portrait files containing duplication and considered together for one coherent disposition. Its boundary limits disposition authority.

A Suggestion is a promising place to look. Current Suggestions initially open Branch Pair projections found by targeted Seed/Counterpart analysis. The exact Suggested scopes need not be the final Case boundary.

This distinction is deliberate. Real-corpus experiments showed that a statistically strong broad pair can be less comprehensible than a nearby narrower pair. The Explorer therefore lets the user adjust scope rather than forcing the first suggested boundary.

## Navigation

### Pivot

Pivot changes projection/focus without changing the Working Portrait.

Current choices include:

    Corpus Roots
    Group
    Containing Directory
    Branch
    Best Directory Pair
    Best Branch Pair
    Current Suggested Branch Pair

### Suggest

`Suggest` cycles through current Suggestions. It changes focus only and does not create a portrait operation or require the user to resolve anything.

### Breadcrumbs

Directory and Branch projections expose ancestry within the Corpus. In pair projections each side has independent breadcrumbs. This is a primary mechanism for broadening or narrowing a Suggested relationship until the scope is recognizable and useful.

### Back / Forward

Controls are present but navigation-history behavior is not yet implemented.

## Selection

Selection is persistent across projections and always denotes files in the current Working Portrait. Selecting a Group or higher structural node means selecting its represented descendant files.

`Invert Selected Copies` inverts selection among complete Groups containing selected files. `Invert All Groups` inverts every represented file in the Groups projection.

## Exclude

Exclude removes selected files from the Working Portrait without creating filesystem Actions. The files remain physically present and return if the operation is undone.

## Delete

Delete removes selected files from the Working Portrait and adds deletion Actions. Physical content-loss consequences are summarized before Execute rather than interrupting ordinary portrait editing.

## Move

Move is duplicate-motivated structural relocation, not general-purpose file management. It is available in pair projections; the two scopes explicitly establish source and destination correspondence.

For source scope `S`, destination scope `D`, and selected source file `f`:

1. compute `f`'s relative parent-directory path beneath `S`;
2. apply it beneath `D` to obtain the exact destination Directory;
3. if the same ContentId already exists directly there, retain the destination and reduce the source to Delete;
4. otherwise use the source filename;
5. free path -> Move;
6. same filename and same ContentId -> retain destination, Delete source;
7. same filename and different content -> collision; leave both unchanged and continue.

The identical-content search is limited to the exact computed destination Directory. Berries never invents a filename and never overwrites different content.

## Immediate portrait update

Exclude/Delete/Move update the Working Portrait immediately. There is no Apply step.

After a command:

1. rebuild the visible projection from the new Working Portrait;
2. rebind persistent selection;
3. invalidate derived Directory/Branch/Suggestion analysis;
4. recompute that analysis in the background;
5. restore analysis-dependent capabilities when results arrive.

The Explorer remains usable; background analysis does not take over the current view.

This repeated resolve -> shrink -> re-analyze cycle is intentional. Empirical work showed that a small number of useful structural resolutions can reduce very large duplicate problem sets dramatically.

## Undo

One user command is one Undo step, even when it affects multiple files. Undo removes the latest top-level portrait operation and deterministically rebuilds Working Portrait, Groups, filesystem Actions, and selection binding.

Undo is separate from navigation history.

## Execute

Execute is the physical commitment boundary.

Before execution, Berries asks for explicit approval and reports planned filesystem Action count and Groups with no surviving physical file after the plan.

Execution attempts accumulated Actions without a global filesystem rescan/reconciliation pass. Failures are handled locally and independent safe work continues. A Move first attempts filesystem Move; on `IOException` it falls back to Copy then Delete, and the source is not deleted if Copy fails.

After execution, Berries reports completed Actions, skipped dependent Actions, and failures.

## Semantic context

The user may recognize a Case as backup, migration, reorganization, archive, staging residue, or another Situation. Such recognition can make the natural disposition obvious, but Berries does not require Situation classification before direct Explorer operations.

`SEMANTIC-RESEARCH.md` retains this research because it remains useful for examples, future explanation, and evaluating whether a suggested Case is comprehensible.

## Unique files

Unique files remain modeled because they contribute to structural statistics and can constrain operations such as Move collisions. Whether they should remain members of structural Cases is unresolved and is not settled by the current workflow.

## Save / Load

Save/Load remains unimplemented. A future saved session should restore modeled session state directly; a user wanting a fresh filesystem view should start a new session and scan again.
