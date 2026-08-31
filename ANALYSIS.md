# Berries Analysis Design

This document defines how Berries discovers Groups, derives structural evidence, and finds useful Suggestions for the Explorer. Terminology and invariants are defined in `MODEL.md`; interaction and execution are defined in `WORKFLOW.md`.

## Purpose

Analysis exists to reduce the user's effort in resolving duplication. It does this by finding promising places to look, not by prescribing a mandatory workflow or claiming to infer semantic intent.

The Explorer is the stable interaction surface. Analysis supplies evidence and Suggestions; the user may accept a suggested scope, adjust it, Pivot elsewhere, or follow nearby structure.

The practical presentation goal is:

    find a comprehensible question whose resolution is likely to simplify
    a substantial part of the remaining duplicate problem

## Cases, priority, and the historical leverage idea

A **Case** is an objective bounded set of current-Portrait files containing duplication and considered together for one coherent disposition. The Case boundary limits disposition authority.

Very early work identified an important objective: one user decision that determines the disposition of many duplicate files does more useful work than asking one question per duplicate pair. This was called **leverage** and was initially quantified by the number of duplicate file instances in a Case.

That idea remains useful as motivation, but raw leverage did not reliably produce the best presentation order. A broad Branch Pair can cover many duplicate files while being a poor human question; a nearby narrower scope can make the relationship and natural disposition much easier to recognize.

For that reason Berries should not treat one generic `Leverage` value as the governing ranking quantity. Different stages use different numerical devices for different purposes:

    Seed priority
        where is it promising to search?

    Counterpart / Branch Pair score
        how strong is this relationship for a particular Seed?

    Case presentation priority
        which comprehensible Case is most useful to present next?

The current implementation has explicit Seed and pair scores. It does not yet expose a separate general Case-priority scalar.

Historically, some expensive exact leverage computations were replaced with cheaper measures because the cheaper values preserved the useful sort order in practice. That history explains why older formulas bearing the name `Leverage` were not mathematically identical. The requirement was ranking utility, not numerical identity.

## Initial acquisition and Group discovery

Selected roots are normalized to a minimal disjoint Corpus.

`BuildInitialPortraitAsync()` enumerates regular files beneath those roots, applying `Berries.config [exclude]` filtering during acquisition.

Group discovery:

1. groups files by length;
2. skips singleton size groups for hashing;
3. hashes candidates with SHA-256;
4. groups equal hashes;
5. retains groups containing at least two files as Groups;
6. attaches ContentId to grouped files in the session Portrait.

Expected read failures (`IOException`, `UnauthorizedAccessException`, `SecurityException`) evict the affected file from the session. Programming failures propagate.

## Directory analysis

`DirectoryRecord` contains direct-Directory statistics:

    Path
    FileCount
    GroupedFileCount
    GroupCount

`DirectoryPair` describes two exact Directories sharing Groups directly:

    First
    Second
    SharedGroupCount

`SharedGroupCount` is exactly what it says; it is not called leverage. Directory graph diagnostics retain inexpensive measurements such as degree, weighted degree, maximum/mean shared Group count, strongest-pair concentration, connected components, and pair density.

## Branch statistics

`BranchStatisticsAnalyzer` derives first-class statistics for Group-bearing Branches without enumerating all Branch Pairs:

    Path
    ParentPath
    FileCount
    DirectoryCount
    GroupedFileCount
    GroupCount
    GroupedDirectoryCount

`GroupCount` is distinct across the entire Branch. These statistics are cheap enough to use as the first stage of targeted structural discovery.

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

This score answers only:

    where is it worth looking for a strong relationship?

It is critical that Seed rank not be treated as final Suggestion order.

## Why Seeds and Counterparts exist

Exhaustive Branch Pair construction was abandoned after real-corpus experiments produced combinatorial populations and long runtimes without proportional user value.

More importantly, experiments showed that the useful human boundary often cannot be selected by a single global "largest problem first" measure. Promising local structure is easier to identify statistically. Once a relatively small set of promising Branches is found, strong relationships among them can be found cheaply and tend to include the Cases a human would choose heuristically.

That is the purpose of Seed/Counterpart discovery: it is not merely a performance optimization for an otherwise unchanged exhaustive algorithm. It is the empirically successful route to useful questions.

## Targeted Branch Counterpart discovery

Current algorithm:

1. Rank eligible Seeds by `ExcessConcentratedGroups`.
2. In each selection round inspect the top 10 unblocked Seeds.
3. For each Seed, find eligible non-nested Counterparts sharing Groups.
4. Compute for each Seed/Counterpart relationship:

       shared Group count
       Seed coverage
       Counterpart coverage
       Jaccard overlap
       score = shared Group count * Jaccard

5. Keep the strongest few Counterparts for each Seed.
6. Compare the best Branch Pair from every Seed in the current top-10 window.
7. Select the strongest Branch Pair across that window as the next Suggestion.
8. Use Seed priority only as a later tie-breaker after pair score.
9. Block the chosen Seed and chosen highest-scoring Counterpart families and repeat.

The best Branch Pair is often **not** produced by the highest-ranked Seed. This behavior is intentional and protected by a regression test.

The analyzer also records exact-root Directory Pair shared-Group count as a diagnostic when available; it does not drive Branch Pair ranking.

## Suggestions

A `BranchPairSuggestion` contains the Seed used to reach a relationship and its ranked Counterparts. The highest-scoring Counterpart forms the Branch Pair initially presented for that Suggestion.

A Suggestion is not necessarily the final Case boundary. It is a promising place to begin looking. Pair breadcrumbs and Pivot let the user broaden or narrow scope, inspect related structure, and arrive at a more comprehensible Case before acting.

Future Suggestion sources may include strong Group-centric or same-Directory signals, but they should enter through the same Explorer rather than create special wizard stages.

## On-demand best Branch Pair

When the user Pivots from an explicitly selected Branch to `Best Branch Pair`, Berries searches non-nested Branches on demand using the same Group-overlap/Jaccard idea.

Here the selected Branch is simply one side of the requested Branch Pair. It is not thereby a Seed in the targeted Suggestion algorithm.

## Empirical collapse of the problem

A central R&D result is that the remaining problem often shrinks extremely rapidly when useful structural Cases are actually resolved. Large datasets containing on the order of tens of thousands of duplicate instances could often be potentially resolved through roughly a handful of Cases rather than one decision per duplicate pair.

This observation has several architectural consequences:

- optimize for useful next questions rather than a complete global plan;
- re-analyze the current Working Portrait after resolutions rather than over-optimizing the original portrait;
- prefer comprehensible scope over a slightly larger but obscure raw duplicate count;
- avoid exhaustive Branch Pair generation;
- keep Suggestions advisory so the user can exploit semantic recognition the statistics cannot provide.

## Projection queries

`PortraitQueries` answers factual questions about the current Working Portrait, including Groups, files in Directory/Branch scopes, breadcrumbs, best Directory Pair, Branch counterpart eligibility, and shared Group counts.

`Berries.Projection` turns those facts into UI-independent projection models. `ProjectionState` records presentation/navigation state only; it is not a Case.

## Derived analysis after portrait operations

Exclude/Delete/Move immediately rebuild the Working Portrait and current Groups. Directory analysis, Branch statistics, and Suggestions then become stale.

Current behavior:

1. a portrait command changes `BerriesSession`;
2. `BerriesApplication` invalidates the derived result objects;
3. the GUI immediately refreshes the visible projection;
4. Directory analysis, Branch statistics, and Suggestion discovery recompute in the background;
5. completed results restore capabilities such as Suggest.

Known ContentId values are not reread or rehashed after virtual portrait operations.

The initial scan is still sequential through derived analysis. A more explicit dependency/validity model remains the next architectural topic after terminology cleanup.

## Unique-file question

The current structural statistics include ordinary `FileCount`, so unique files influence concentration measures even though they are not Group-resolution targets. Earlier Case definitions also allowed unique files within structural Case bounds.

Whether unique files should remain Case members is unresolved. Do not remove them from the model or ranking mathematics merely as a terminology cleanup; their analytical role must be evaluated separately.

## Performance lessons retained

- Group-centric and container-centric views are complementary.
- Repeated low-level Groups can manufacture weak higher-level structure.
- Branch statistics are cheap and useful before relationship search.
- Seed priority and Branch Pair quality are distinct measurements.
- Evaluating several good Seeds before choosing a Suggestion materially improves results.
- Targeted Seed/Counterpart search is dramatically cheaper and more useful than exhaustive Branch Pair construction.
- Exact maximum duplicate count is not necessarily the most comprehensible Case.
- Repeated resolution plus re-analysis can collapse very large duplicate problems quickly.
- Berries should surface useful Suggestions, not pretend to infer semantic truth autonomously.
