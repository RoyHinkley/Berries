# <img src="artwork/berries.svg" alt="berries logo" height="24px" width="auto"> Berries Project

## Problem statement

Ordinary duplicate-file tools generally present groups of identical files and leave the user to decide what to do with individual copies. That works for isolated duplication, but poorly for accumulated backups, reorganized trees, partial moves, migrations, archives, copied directory trees, generated material, and other real filesystem histories.

Berries treats duplicate content as evidence about both files and filesystem structure. It builds a virtual Working Portrait of the user's desired Corpus and presents that portrait through an Explorer in which the user can navigate among Groups, Directories, Branches, Directory Pairs, and Branch Pairs; select duplicate files; and Exclude, Delete, or Move them before any physical filesystem change occurs.

## Objective

Provide a safe and practical way to understand and resolve unwanted file duplication across large filesystem trees while preserving the content and organization the user wants.

The system should reduce required user attention without pretending to infer semantic truth or making autonomous destructive decisions.

## User-facing vocabulary

The UI deliberately uses ordinary filesystem language rather than exposing every internal domain type.

    Group
        all currently duplicated files having identical content
        (internal model: DuplicateSet / ContentId)

    file / copy
        one filesystem instance shown in a Group or structural view
        (internal model: FileInstance)

    Directory
        one exact directory

    Branch
        a directory together with all descendants

    Directory Pair
        two exact directories sharing duplicated Groups

    Branch Pair
        two branches sharing duplicated Groups

    Corpus Roots
        the selected top-level trees that define the Corpus

Internal names remain useful in Core and analysis code, but documentation describing user interaction should prefer the UI terms above.

## Governing principles

1. **Corpus is logical.** Selected roots define the material Berries scans. Configuration or interactive Exclude removes material from the working Corpus without changing disk.

2. **Portrait-first design.** A scan establishes a fixed Initial Portrait. The Working Portrait is deterministically reconstructed from it plus the ordered portrait-operation history.

3. **The Explorer is primary.** The Explorer is the long-lived work surface. Suggestions and structural analyses provide useful places to look; they do not impose a mandatory queue.

4. **Selection has one operational meaning.** Across views, selection resolves to duplicate files. Structural nodes are convenient scopes over applicable descendant files.

5. **Projection is navigation.** Group, Directory, Branch, Directory Pair, Branch Pair, and Corpus Roots are different organizations of the same Working Portrait. Pivot changes view/focus, not resolution semantics.

6. **Operations are explicit and positive.** The working portrait operations are Exclude, Delete, and Move. There is no Keep operation: a file survives when no destructive operation removes or relocates it.

7. **There is no user-facing settlement layer.** The application does not ask the user to Accept or Settle duplicate relationships. Exclude removes files that should no longer participate in Berries.

8. **There is no Apply layer.** Exclude/Delete/Move update the Working Portrait immediately and are Undoable. Execute is the separate physical commitment boundary.

9. **Move preserves source-relative structure.** A pair view establishes explicit source and destination scopes. Existing destination organization is authoritative. Berries does not invent filenames or overwrite different content.

10. **Unique files remain known to the session.** They are not duplicate-resolution targets, but they can occupy destination paths and therefore constrain Move.

11. **Analysis serves attention.** Structural analysis should find useful questions without exhaustive enumeration. Practical targeted analysis is preferred over mathematically comprehensive pair generation when the latter creates combinatorial cost without corresponding user value.

12. **Execution is explicit.** No physical filesystem modification occurs until Execute. The user approves a summary first; execution then attempts the plan, preserves dependency safety, continues independent safe work after local failures, and reports actual outcomes.

13. **Core remains independent of the GUI and platform-specific filesystem behavior.** Projection construction is also separated from GUI control logic.

## Current application flow

The implemented shell contains a root-selection view and the Duplicate Explorer.

For a new session:

    Select Roots...
        -> Add / Remove roots
        -> Explore
        -> Corpus view appears immediately
        -> filesystem enumeration
        -> size grouping and hashing
        -> Groups/session established
        -> directory, Branch, and counterpart analysis
        -> Group view becomes the normal starting projection
        -> Pivot / Suggest / Navigate / Resolve
        -> Execute when ready

The current initial scan is orchestrated sequentially by `BerriesApplication.ScanAsync()`: acquisition and duplicate discovery are followed by directory analysis, Branch statistics, and targeted counterpart analysis before `ScanAsync()` returns. The GUI remains responsive and reports progress, but the full derived-analysis chain is presently part of initial scan completion.

After Exclude/Delete/Move/Undo changes the Working Portrait, the visible projection is refreshed immediately and the derived structural analysis is recomputed in the background. Background completion updates capabilities such as Suggest without taking control of the user's current view.

`Load Saved Session...` and `Save Session...` are present but disabled. Persistence remains deferred.

## Duplicate Explorer

### Implemented projections

**Groups** — one pane. Each Group contains every currently duplicated file having one content identity. File leaves show full paths.

**Directory** — one pane rooted at one exact directory, showing duplicate files directly contained there.

**Branch** — one pane rooted at a directory and recursively organizing applicable duplicate files beneath it.

**Directory Pair** — two equivalent panes rooted at two exact directories.

**Branch Pair** — two equivalent panes rooted at two branches.

**Corpus Roots** — one pane containing the selected Corpus roots as Branch-style trees.

### Navigation

The current Explorer supports Pivot among the projections above, breadcrumb navigation for structural views, and Suggest for targeted Branch-Pair candidates. Back/Forward controls are present in the shell but navigation-history behavior is not yet implemented.

`Suggest` means "identify something likely to reward attention," not "give me the next required task."

### Selection and resolution

Selection is shared across projections and is expressed in duplicate files. The UI currently provides:

    Invert Selected Copies
    Invert All Groups
    Exclude
    Delete
    Move ->
    <- Move
    Undo

Exclude removes selected files from the Working Portrait without producing physical Actions. Delete removes them from the Working Portrait and contributes delete Actions. Move transforms the Working Portrait and contributes physical move work as appropriate.

## Move behavior

Move operates from one explicit structural scope to another. For each selected source file, Berries preserves the source-relative directory path beneath the chosen destination scope.

Within each exact computed destination directory:

- if the same content already exists there under any filename, the destination is authoritative and the source reduces to Delete;
- otherwise the source filename is used;
- if that exact destination path is free, the source is moved there;
- if the path contains the same content, the source reduces to Delete;
- if the path contains different content, the collision is reported and that source is left unchanged while other requested files continue.

Berries never invents a filename and never overwrites different content.

## Analysis strategy

Duplicate discovery uses size grouping followed by SHA-256 hashing of non-singleton size groups. Files that cannot be read for expected access reasons are omitted from the established session.

Current structural analysis consists of:

    direct Directory records and Directory Pairs
    Branch statistics
    Branch seed priority
    targeted Branch counterpart search

Comprehensive Branch-Pair enumeration was deliberately abandoned after real-corpus experiments demonstrated combinatorial growth without proportional user value.

Branch statistics are computed independently of Branch-Pair enumeration. Current seed priority uses the parent-relative concentration measure implemented by `BranchPriorityMetrics`. Targeted counterpart analysis then searches promising seeds for non-nested branches sharing duplicated content and ranks actual relationships by shared distinct content and overlap.

The implemented Suggest command cycles through the resulting compact Branch-Pair candidate list. Pivot can also request the best Branch Pair for a selected Branch on demand.

`ANALYSIS.md` contains the detailed current analysis mechanics and empirical findings.

## Execution model

The Working Portrait is a virtual design. Portrait operations maintain the physical Action list required to realize that design:

    Exclude -> no filesystem Action
    Delete  -> DeleteFileAction
    Move    -> MoveFileAction (and executor-level copy/delete handling where required)

Before Execute, Berries shows the number of planned Actions and the number of Groups whose content would have no surviving physical file after the plan, then asks for approval.

Execution does not perform a global filesystem rescan. It attempts the approved Actions against current disk state. Independent failures do not stop unrelated safe work. Dependent destructive work is suppressed when its prerequisite fails. A post-execution summary reports completed, skipped, and failed work.

## Persistence direction

The application shell reserves Save/Load commands but they are currently disabled. If persistence is implemented, a saved session should restore enough state to resume directly rather than silently rescanning and reconciling with the filesystem. The runtime model should remain the authority for any future serialized representation; no persistence schema is currently frozen.

## Design documents

### [MODEL.md](MODEL.md)

Authoritative domain vocabulary and invariants, including the mapping between user-facing terms and internal Core types.

### [ANALYSIS.md](ANALYSIS.md)

Current duplicate discovery and structural-analysis mechanics, practical ranking strategy, performance lessons, invalidation behavior, and analysis lifecycle.

### [WORKFLOW.md](WORKFLOW.md)

Implemented application flow, Explorer projections, selection/navigation, Exclude/Delete/Move/Undo, and Execute behavior.

### [SITUATIONS.md](SITUATIONS.md)

Historical semantic research retained for future use. Situation/Resolution/Disposition classification is not part of the current required Explorer workflow.

### [BOUNDARY.md](BOUNDARY.md)

Empirical findings about useful problem scope and generated/application-managed material.

### [DEVELOPMENT.md](DEVELOPMENT.md)

Current implementation map, component responsibilities, known transitional internals, and deliberately deferred work.

## Platform and architecture

Implementation platform:

    C#
    .NET 10
    Avalonia

Solution decomposition:

    Berries.Core
        domain/session model, Portrait operations, duplicate and structural
        analysis, queries, planning/execution contracts

    Berries.Projection
        UI-independent construction and querying of Explorer projections

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows filesystem adapter

    Berries.Gui
        Avalonia desktop shell, interaction orchestration, and presentation

    Berries.Core.Tests
        platform-independent Core tests using synthetic filesystem/Portrait data

Architectural test:

    If Core analysis/session behavior cannot be exercised against synthetic
    data without Avalonia or Windows-specific assumptions, a boundary has leaked.
