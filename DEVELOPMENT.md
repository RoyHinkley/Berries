# Development

The governing design is in `PROJECT.md`, `MODEL.md`, `ANALYSIS.md`, and `WORKFLOW.md`. `SITUATIONS.md` and `BOUNDARY.md` retain semantic/empirical research. This file describes the current implementation and, importantly, where it differs from the now-settled next design.

## Solution

- `Berries.Core` — platform/UI-independent domain and analysis.
- `Berries.FileSystem.Abstractions` — platform-neutral filesystem boundary.
- `Berries.FileSystem.Windows` — Windows filesystem adapter.
- `Berries.Gui` — Avalonia desktop front end and orchestration.
- `Berries.Core.Tests` — synthetic platform-independent Core tests.

There is deliberately no console front end. Target framework is .NET 10. GUI is Avalonia and built as `WinExe`.

## Current implemented discovery pipeline

The existing GUI can:

1. normalize selected Corpus roots;
2. construct a Portrait while applying `Berries.config` filtering;
3. discover DuplicateSets by size grouping and SHA-256 hashing;
4. identify widely distributed same-name DuplicateSets;
5. apply current settlement machinery from the experimental checklist;
6. analyze direct-directory relationships;
7. derive first-class Branch statistics;
8. rank promising Branch seeds;
9. perform targeted Branch counterpart search and report candidates.

Comprehensive BranchPair generation is currently suspended for the targeted-discovery experiments and should remain so in the next design.

The current special distributed-DuplicateSet checklist and `DuplicateSettlements` are obsolete relative to the governing design. They were valuable experiments and should be removed/replaced rather than extended.

## Configuration: rename ignore to exclude

Current code/config uses `[ignore]`. The governing format is now `[exclude]`.

Retain the existing useful matching semantics:

    no separator
        match any path component / filename

    separator present
        match contiguous path segment in the full path

    * and ?
        wildcards

    # or ;
        comments

No compatibility requirement exists for `[ignore]` at this stage.

Configuration exclusion has the same semantic effect as interactive Exclude: the matching FileInstance is not part of the working Corpus. For efficiency, configuration exclusions may continue to be filtered during acquisition rather than materialized and then removed.

## Duplicate discovery and accessibility

Duplicate discovery groups by length and hashes only non-singleton groups. Equal SHA-256 hashes form DuplicateSets.

Expected access failures while reading a file (`IOException`, `UnauthorizedAccessException`, `SecurityException`) evict that file from the working session; programming failures propagate. Windows `OpenRead` uses permissive sharing (`FileShare.ReadWrite | FileShare.Delete`).

Unique files should remain known to the session even though they are not duplicate-resolution targets. Move needs them for destination collision detection.

## Branch statistics

`BranchStatisticsAnalyzer` derives:

    Path
    ParentPath
    FileCount
    DirectoryCount
    DuplicateFileCount
    DuplicateContentCount
    DuplicateDirectoryCount

`DuplicateContentCount` is distinct across the branch, not a sum of descendant directory counts.

`BranchPriorityMetrics` experiments included several parent-relative measures. The current preferred seed metric is:

    C = duplicated-Content retention / file retention
    D = distinct duplicated Content in child Branch
    seed score = D * (1 - 1/C), for C > 1; otherwise 0

This metric repeatedly surfaced known meaningful structures in real corpora and is the current basis for targeted seed selection.

## Targeted counterpart analysis

`BranchCounterpartAnalyzer` is the current direction for container-centric discovery.

Current behavior/design:

- rank eligible Branch seeds by the preferred seed score;
- inspect a top-10 seed window each selection round;
- find each seed's best non-nested counterpart from distinct duplicated-Content overlap;
- pair score = shared distinct Content * Jaccard;
- choose the highest pair score from the window;
- cull/block both selected branch roots and descendants;
- repeat for a compact shortlist;
- retain a few top counterpart candidates for diagnostics;
- direct exact-root `DirectoryPair.SharedContentCount` may be reported diagnostically but does not drive ranking.

This distinction is important:

    seed score -> where should we look?
    pair score -> did we find a promising relationship?

Real-corpus testing showed the winning pair can originate from a lower seed rank within the window rather than the highest seed.

## Performance findings

Exhaustive BranchPair generation proved combinatorial. Representative experiments ranged from tens of thousands of pairs in small source trees to roughly 15 million pairs and tens of minutes in a large archive corpus.

By contrast, Branch statistics are cheap and targeted counterpart discovery has been on the order of seconds or less on large corpora while surfacing recognizable relationships.

Do not restore exhaustive BranchPair generation merely to preserve the older Case model.

## Next implementation revision: Duplicate Explorer

The next revision should shift from report/dialog-driven experimentation to the governing Explorer model.

### Stable application shell

Add/retain a conventional `File` menu with intended commands:

    Select Roots...
    Load Saved Session...   [initially nonfunctional/disabled is acceptable]
    Save Session...         [initially nonfunctional/disabled is acceptable]
    Execute...
    Exit

`Select Roots...` displays the current Add / Remove / Scan root-selection interaction.

After Scan begins/completes enough acquisition to establish the session, transition to the Duplicate Explorer. The Explorer should become the long-lived work surface rather than creating a sequence of special analysis dialogs.

### Status/progress

Move scan/analysis progress into a persistent bottom status bar. It should expose current stage and progress within that stage.

Preserve analysis-stage boundaries so heavier work can migrate to asynchronous background tasks. Full concurrency is not required in the first revision, but do not build a UI architecture that assumes one monolithic blocking Scan must finish before the Explorer exists.

### Remove special exclusion screen

The current distributed-DuplicateSet checklist should disappear. Its use case belongs in Content projection plus ordinary Exclude.

### Projections

Implement toward:

    Content
        one pane; Content roots -> full-path FileInstance leaves

    DirectoryPair
        two panes rooted at exact directories

    BranchPair
        two panes rooted at branches

Every selection resolves to duplicate FileInstances. Higher structural nodes are scope shortcuts, not directory-operation targets.

### Commands

Design toward:

    Back / Forward
    Pivot
    Suggest Case
    Invert Selection
    Exclude
    Delete
    Move -> / <- Move
    Undo

There is no Keep, Accept/Settle, or Apply.

Exclude/Delete/Move immediately transform the Working Portrait. Undo reverses portrait operations. Back/Forward handles navigation history.

### Suggest Case

Suggestions are analysis-produced foci, not a queue. Enable the command when enough analysis is available. The current Content heuristics and targeted Branch counterpart analyzer are candidate suggestion sources.

## Move implementation requirements

Move semantics are now settled and should be implemented exactly from `MODEL.md` / `WORKFLOW.md` rather than inferred from ordinary filesystem Move semantics.

For each selected source duplicate instance:

1. map its relative directory path from source scope beneath destination scope;
2. if identical Content already exists directly within that computed destination directory, delete the source and preserve the existing destination name;
3. otherwise use the source filename at that directory;
4. free path -> Move;
5. same filename + same Content -> Delete source;
6. same filename + different Content -> flag immediately, leave both untouched, continue other instances.

Do not search descendant destination directories for same Content. `Within` means the exact computed directory.

Do not invent filenames, rename, or overwrite different Content.

## Portrait operation model

The next Core model should make the distinction explicit:

    Initial Portrait
        fixed scan result

    ordered portrait operations
        Exclude / Delete / Move

    Working Portrait
        deterministic result; user's desired Corpus

The existing settlement model is transitional code and should not dictate the new architecture.

Exclude contributes no filesystem Action. Delete and Move contribute physical work for Execute.

## Execute direction

Physical execution is not the first target of the Explorer revision, but the architecture should preserve the settled model:

- pre-execution summary before approval, including planned Content losses;
- no early last-instance warning during ordinary Delete editing;
- no global filesystem revalidation/rescan before execution;
- attempt the approved plan;
- preserve dependency safety (especially copy-before-delete for cross-device Move);
- if a prerequisite fails, omit dependent destructive work;
- continue independent safe operations;
- post-execution report records actual successes/failures/exceptions.

## Save/Load direction

Save/Load may remain nonfunctional. If implemented later, serialize one session object as compressed JSON so it can be loaded directly into the application model.

Loading does not reconcile with current disk state. A fresh scan is a New Session.

Avoid designing a detailed persistence schema during the Explorer rewrite unless actual use demonstrates the need.

## Tests during the rewrite

Preserve the existing useful duplicate-discovery, filesystem-boundary, Branch-statistics, and targeted-counterpart tests.

Replace settlement-centric tests as the new Exclude/Working-Portrait operation model lands.

Add focused synthetic tests for:

- Exclude removing instances from duplicate analysis without filesystem Actions;
- Undo of Exclude/Delete/Move;
- Content-relative Invert Selection logic if implemented in Core/view-model code;
- Move relative-path preservation;
- Move destination-directory same-Content detection independent of filename;
- exact-directory (`within`, not descendant) semantics;
- same-name/same-Content collision reducing to source deletion;
- same-name/different-Content collision leaving both untouched while other moves proceed;
- collision detection against unique destination files;
- cross-device execution dependency: failed copy must suppress source delete when execution is implemented.

## Immediate goal

Bring the code into alignment with the now-settled portrait-first Duplicate Explorer design without attempting every deferred feature at once.

The first revision should establish the correct long-lived shell, session/Portrait model, projection foundation, ordinary Exclude/Delete/Move command architecture, status/progress location, and targeted suggestion path. Visual polish, Save/Load, full asynchronous optimization, and physical Execute can follow once the interaction model is exercised against real corpora.
