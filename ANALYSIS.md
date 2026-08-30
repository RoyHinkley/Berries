# Berries Analysis Design

This document defines how Berries discovers Groups, derives structural evidence, and finds Suggestions for the Explorer. Terminology and invariants are defined in `MODEL.md`; interaction and execution are defined in `WORKFLOW.md`.

## Purpose

Analysis exists to help the user find duplication likely to reward attention. Its application-level output is a set of Suggestions; it does not prescribe a mandatory workflow.

A Suggestion is a view Berries has found worth presenting because its structure indicates that one or a few user decisions may resolve a relatively large amount of duplicated material. The currently implemented Suggestions are Branch Pair views.

The useful viewpoints are complementary:

    Group-centric
    same-Directory
    Branch/container-centric

The governing presentation idea is:

    identify something likely to reward attention

and, when choosing among comparable possibilities:

    prefer the smallest comprehensible question with substantial downstream
    simplifying effect

## Initial acquisition and Group discovery

Selected roots are normalized to a minimal disjoint Corpus.

`BuildInitialPortraitAsync()` enumerates regular files beneath those roots, applying `Berries.config [exclude]` filtering during acquisition. The Portrait records path, parent Directory, length, and observed write time.

Group discovery then:

1. groups files by length;
2. discards singleton size groups from hashing work;
3. hashes files in non-singleton size groups with SHA-256;
4. groups equal hashes;
5. retains hash groups containing at least two files as Groups;
6. attaches the resulting ContentId to grouped files in the session Portrait.

Expected read failures (`IOException`, `UnauthorizedAccessException`, `SecurityException`) evict the affected file from the session. Programming failures propagate.

Windows content reads request permissive sharing (`ReadWrite | Delete`) so files already open by other applications can often still be hashed.

The resulting Group discovery types are:

    GroupDiscoveryProgress
    GroupDiscoveryTiming
    GroupDiscoveryResult

A Group is the user/domain concept. `ContentId` remains the narrower internal identity used to establish and track byte-equal content.

## Configuration Exclude

`Berries.config` uses an `[exclude]` section.

Current matching semantics:

    pattern without path separator
        match any path component / filename

    pattern containing / or \
        match a contiguous path segment anywhere in the full path

    * and ?
        wildcard matching

    # or ; at line start
        comment

Configuration Exclude and interactive Exclude have the same logical effect: excluded material does not participate in the Working Portrait. Configuration filtering happens earlier simply to avoid needless acquisition and hashing work.

## Directory analysis

Directory statistics describe directly contained files only.

`DirectoryRecord` contains:

    Path
    FileCount
    GroupedFileCount
    GroupCount

`DirectoryPair` describes two exact Directories sharing one or more Groups directly:

    First
    Second
    SharedGroupCount

There is no separate abstract "leverage" value here. Pair strength is the directly meaningful count of shared Groups.

### Directory graph diagnostics

The Directory Pair network retains inexpensive graph measurements because they proved useful during empirical work and diagnostics:

    degree
    weighted degree
    maximum shared Group count
    mean shared Group count
    strongest-pair concentration
    connected components
    pair density

These values are evidence, not semantic classification.

Same-Directory copies remain directly visible through a Directory projection even though no Directory Pair is needed to express them.

## Branch statistics

`BranchStatisticsAnalyzer` derives first-class statistics for Group-bearing Branches without enumerating all Branch Pairs:

    Path
    ParentPath
    FileCount
    DirectoryCount
    GroupedFileCount
    GroupCount
    GroupedDirectoryCount

`GroupCount` is a distinct Group count across the entire Branch. A Group represented in multiple descendant Directories contributes once to the Branch's GroupCount.

Branch statistics are obtained by walking ancestry within the Corpus and are inexpensive compared with exhaustive Branch relationship construction.

## Branch Seed priority

For a child Branch relative to its immediate parent:

    D = child GroupCount

    group retention = child GroupCount / parent GroupCount
    file retention  = child FileCount / parent FileCount

    C = group retention / file retention

The current useful Seed score is:

    D * (1 - 1/C), for C > 1
    0,             otherwise

This is stored as `ExcessConcentratedGroups`.

Interpretation: Groups are concentrated in the child beyond what would be expected from the child's ordinary share of parent files.

The Seed score answers only:

    where is it worth looking for a strong Branch relationship?

It does not measure the quality of any particular Counterpart or Branch Pair.

## Targeted Branch Counterpart discovery

Exhaustive ancestor-Cartesian Branch Pair construction was abandoned after experiments produced very large pair populations and long runtimes without proportional user value.

Current targeted analysis:

1. Rank eligible Branch Seeds by `ExcessConcentratedGroups`.
2. In each selection round, inspect the top 10 unblocked Seeds.
3. For each Seed, identify non-nested candidate Counterparts sharing Groups.
4. For each Seed/Counterpart relationship compute:

       shared Group count
       Seed coverage
       Counterpart coverage
       Jaccard overlap
       score = shared Group count * Jaccard

5. Keep the strongest few Counterparts for each Seed.
6. Compare the best Branch Pair from every Seed in the current top-10 window.
7. Emit the strongest of those Branch Pairs as the next Suggestion.
8. Block the chosen Seed Branch, chosen Counterpart Branch, and their descendants from later Seed selection.
9. Repeat until the requested Suggestion limit is reached or no candidate remains.

The best Branch Pair often does not originate from the highest-ranked Seed. Seed quality and pair quality are deliberately separate measurements; Seed rank is only a later tie-breaker after pair score.

The analysis also records exact-root Directory Pair shared-Group count as a diagnostic when available; it does not drive Branch Pair ranking.

## On-demand best Branch Pair

When the user Pivots from a selected Branch to `Best Branch Pair`, Berries searches non-nested Branches on demand using the same Group-overlap/Jaccard idea.

This operation starts from the selected Branch rather than from the ranked Seed search and does not require a pre-enumerated population of Branch Pairs.

## Suggestions and Suggest

A `BranchPairSuggestion` contains the Seed used to reach the relationship and its ranked Counterparts. The highest-scoring Counterpart forms the Branch Pair presented by that Suggestion.

`Suggest` becomes available when Suggestions are present. It cycles through the compact Suggestion list and opens each corresponding Branch Pair. Suggest changes focus only; the user may immediately Pivot elsewhere or operate on any selection.

Seed and Counterpart are therefore under-the-hood search concepts. Suggestion is the application concept exposed to the user.

Future Suggestion sources may include strong Group-centric or same-Directory signals, but they should enter through the same Explorer rather than create special workflow screens.

## Projection queries

`PortraitQueries` answers factual questions about the current Working Portrait, including:

    Groups
    Groups for current selection
    grouped files in a Directory
    grouped files in a Branch
    grouped files arranged beneath Corpus Roots
    best Directory Pair for a Directory
    whether a Branch can have a Counterpart
    shared Group count between two scopes

`Berries.Projection` turns those facts into UI-independent projection models. Avalonia presentation remains above that layer.

## Derived analysis after portrait operations

Exclude/Delete/Move immediately rebuild the Working Portrait and current Groups in memory. Directory analysis, Branch statistics, and Suggestions then become stale.

Current application behavior is intentionally simple:

1. a portrait command changes `BerriesSession`;
2. `BerriesApplication` invalidates all three derived result objects;
3. the GUI immediately refreshes the visible projection from the Working Portrait;
4. Directory analysis, Branch statistics, and Suggestion discovery are recomputed in the background;
5. completed results restore capabilities such as Suggest.

Known ContentId values do not need to be reread or rehashed after virtual portrait operations.

### Initial scan lifecycle

The initial path is still sequential:

    Corpus / Portrait acquisition
        -> Group discovery
        -> Session construction
        -> Directory analysis
        -> Branch statistics
        -> Suggestion discovery
        -> ScanAsync returns

This is an implementation fact, not a governing requirement.

The current design work is moving toward an explicit dependency/validity model in which the front-end discovery chunk remains stable until the Corpus changes, while derived analyses can be independently invalidated and produced when prerequisites and demand justify them.

## Performance lessons retained

Real-corpus testing established several durable principles:

- repeated low-level Groups can manufacture large amounts of weak structural evidence;
- Group-centric and container-centric views are complementary;
- Branch statistics are cheap and useful before relationship search;
- targeted Seed/Counterpart search is dramatically cheaper than exhaustive Branch Pair construction;
- Seed quality and Branch Pair quality are different measurements;
- evaluating several good Seeds before selecting the next Suggestion materially improves the result;
- mathematically perfect structural boundaries are unnecessary when nearby boundaries support the same useful user operation;
- generated/repository/application-managed trees can create substantial real duplication evidence, making configurable Exclude valuable;
- Berries should surface useful Suggestions, not pretend to infer semantic truth autonomously.
