# Berries Analysis Design

This document defines how Berries discovers Groups, derives structural evidence, and identifies promising Explorer foci. Terminology and invariants are defined in `MODEL.md`; interaction and execution are defined in `WORKFLOW.md`.

## Purpose

Analysis exists to help the user find duplication likely to reward attention. It supplies evidence to the Explorer; it does not prescribe a mandatory workflow.

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

## Branch seed priority

For a child Branch relative to its immediate parent:

    D = child GroupCount

    group retention = child GroupCount / parent GroupCount
    file retention  = child FileCount / parent FileCount

    C = group retention / file retention

The current useful seed score is:

    D * (1 - 1/C), for C > 1
    0,             otherwise

This is stored as `ExcessConcentratedGroups`.

Interpretation: Groups are concentrated in the child beyond what would be expected from the child's ordinary share of parent files.

Seed score answers only:

    where is it promising to look?

It does not measure the quality of any particular counterpart.

## Targeted Branch counterpart discovery

Exhaustive ancestor-Cartesian Branch Pair construction was abandoned after experiments produced very large pair populations and long runtimes without proportional user value.

Current targeted analysis:

1. Rank eligible Branch seeds by `ExcessConcentratedGroups`.
2. In each selection round, inspect the top 10 unblocked seeds.
3. For each seed, identify non-nested candidate Branches sharing Groups.
4. For each relationship compute:

       shared Group count
       seed coverage
       counterpart coverage
       Jaccard overlap
       score = shared Group count * Jaccard

5. Keep the strongest few counterparts for diagnostics.
6. Choose the strongest relationship among the current top-10 seed window.
7. Block the chosen seed Branch, chosen counterpart Branch, and their descendants from later seed selection.
8. Repeat until the requested suggestion limit is reached or no candidate remains.

The winning relationship can originate from any seed in the window. Seed quality and relationship quality are therefore deliberately separate measurements.

The analysis also records exact-root Directory Pair shared-Group count as a diagnostic when available; it does not drive Branch Pair ranking.

## On-demand best Branch Pair

When the user Pivots from a selected Branch to `Best Branch Pair`, Berries searches non-nested Branches on demand using the same Group-overlap/Jaccard idea.

This operation does not require a pre-enumerated population of Branch Pairs.

## Suggest

`Suggest` becomes available when targeted Branch counterpart results are present.

The current implementation cycles through the compact suggestion list and opens the corresponding Branch Pair. Suggest changes focus only; the user may immediately Pivot elsewhere or operate on any selection.

Future suggestion sources may include strong Group-centric or same-Directory signals, but they should enter through the same Explorer rather than create special workflow screens.

## Projection queries

`PortraitQueries` answers factual questions about the current Working Portrait, including:

    Groups
    Groups for current selection
    grouped files in a Directory
    grouped files in a Branch
    grouped files arranged beneath Corpus Roots
    best Directory Pair for a Directory
    whether a Branch can have a counterpart
    shared Group count between two scopes

`Berries.Projection` turns those facts into UI-independent projection models. Avalonia presentation remains above that layer.

## Derived analysis after portrait operations

Exclude/Delete/Move immediately rebuild the Working Portrait and current Groups in memory. Directory analysis, Branch statistics, and counterpart results then become stale.

Current application behavior is intentionally simple:

1. a portrait command changes `BerriesSession`;
2. `BerriesApplication` invalidates all three derived result objects;
3. the GUI immediately refreshes the visible projection from the Working Portrait;
4. Directory analysis, Branch statistics, and counterpart analysis are recomputed in the background;
5. completed results restore capabilities such as Suggest.

Known ContentId values do not need to be reread or rehashed after virtual portrait operations.

### Initial scan lifecycle

The initial path is still sequential:

    Corpus / Portrait acquisition
        -> Group discovery
        -> Session construction
        -> Directory analysis
        -> Branch statistics
        -> counterpart analysis
        -> ScanAsync returns

This is an implementation fact, not a governing requirement.

The current design work is moving toward an explicit dependency/validity model in which the front-end discovery chunk remains stable until the Corpus changes, while derived analyses can be independently invalidated and produced when prerequisites and demand justify them.

## Performance lessons retained

Real-corpus testing established several durable principles:

- repeated low-level Groups can manufacture large amounts of weak structural evidence;
- Group-centric and container-centric views are complementary;
- Branch statistics are cheap and useful before relationship search;
- targeted counterpart search is dramatically cheaper than exhaustive Branch Pair construction;
- seed quality and relationship quality are different measurements;
- mathematically perfect structural boundaries are unnecessary when nearby boundaries support the same useful user operation;
- generated/repository/application-managed trees can create substantial real duplication evidence, making configurable Exclude valuable;
- Berries should surface promising evidence, not pretend to infer semantic truth autonomously.
