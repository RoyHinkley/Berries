# Development

The governing design is split across `PROJECT.md`, `MODEL.md`, `ANALYSIS.md`, `SITUATIONS.md`, `WORKFLOW.md`, and the empirical working document `BOUNDARY.md`. `PROJECT.md` is the short architectural overview and index. This file records the current implementation state and immediate empirical work.

The solution contains:

- `Berries.Core` — platform/UI-independent domain, analysis, Cases, settlement state, planning contracts, and execution-plan contracts.
- `Berries.FileSystem.Abstractions` — the deliberately small platform-neutral filesystem boundary.
- `Berries.FileSystem.Windows` — Windows filesystem adapter.
- `Berries.Gui` — Avalonia desktop front end and GUI-specific orchestration/diagnostics.
- `Berries.Core.Tests` — synthetic, platform-independent tests of Core behavior and boundaries.

There is deliberately no console front end. Target framework is .NET 10. The GUI references Avalonia 12.1.0 and is built as `WinExe`.

## Current analysis pipeline

The GUI maintains a list of Corpus roots. Root normalization removes exact duplicates and descendants of already selected roots.

The `Scan` button now runs the exploratory pipeline in sequence:

1. load `Berries.config` and apply `[ignore]` rules while constructing the Initial Portrait;
2. discover DuplicateSets by size grouping and SHA-256 hashing;
3. cheaply screen for same-name DuplicateSets occurring once each in at least three distinct directories;
4. present those candidates as a checklist asking which should be accepted with the retain-all resolution;
5. apply the checked whole-DuplicateSet settlements;
6. analyze unresolved direct-directory relationships and derive first-class statistics for every duplicate-bearing Branch;
7. construct BranchPairs from the resulting DirectoryPair graph;
8. rank/materialize the top Case sample;
9. build the structural report and append branch-statistics, experimental branch-seed rankings, early-settlement, and configuration summaries.

The old individual `Find duplicates`, `Analyze directories`, `Analyze scopes`, and `Top cases` GUI buttons have been removed. Their controller operations remain separate so analysis stages stay testable and reusable.

The checklist is deliberately an exploratory pre-structural decision pass. Nothing is preselected. Canceling the dialog aborts the remainder of the run. Checked items are settlements for the current run only; persistent filename rules have not yet been implemented.

Each checklist item exposes all instance paths through a hover tooltip so the user can inspect context without expanding the main UI.

## Berries.config ignore filtering

`src/Berries.Gui/Berries.config` is copied beside the GUI executable and loaded from `AppContext.BaseDirectory` at the start of every scan.

The current format is deliberately simple:

    [ignore]
    .git
    bin
    obj
    *.tmp
    .git/objects

Blank lines and lines beginning with `#` or `;` are ignored. Matching is case-insensitive. `*` and `?` wildcards are supported.

A pattern without a path separator is matched against every path component, so a directory-name match excludes all Files beneath that directory and an exact or wildcard filename can exclude that File wherever it occurs. A pattern containing `/` or `\` is matched against a contiguous portion of the full path.

Ignored Files never enter the Initial Portrait and therefore never participate in hashing, DuplicateSets, DirectoryPairs, BranchPairs, or Cases. The filesystem adapter may still enumerate ignored directories internally; traversal pruning is not implemented because the present purpose is corpus control for empirical testing rather than scan optimization.

The structural report records the loaded configuration path and active ignore patterns so runs remain interpretable.

## Duplicate discovery and accessibility

Duplicate discovery first groups Files by length and hashes only non-singleton length groups. Equal hashes form physical `DuplicateSet`s. A DuplicateSet remains a fact about the Portrait even when some or all of its duplicate relationships are later accepted.

When access to a particular File fails with `IOException`, `UnauthorizedAccessException`, or `SecurityException`, that File is evicted from the Current Portrait for the session. Programming failures propagate normally. Windows Content reads request permissive sharing (`FileShare.ReadWrite | FileShare.Delete`).

## Duplicate settlements

`DuplicateSettlements` records duplicate relationships that have been semantically accepted and therefore no longer require a user decision. It supports:

- whole-Content/DuplicateSet acceptance;
- acceptance of one specific equal-Content File pair;
- copying settlement state for non-mutating experiments;
- querying whether any relationship in a DuplicateSet or subset of its Files remains unresolved.

Settlements do not change physical DuplicateSets or the Portrait. They change the unresolved evidence from which structural Cases are derived.

Directory analysis is settlement-aware. A whole accepted DuplicateSet contributes no unresolved directory evidence. A selectively accepted File pair contributes no evidence for that pair while other mates of the same Content remain available.

Case analysis is also settlement-aware: fully accepted DuplicateSets and fully accepted internal relationships no longer generate Cases.

## Early distributed-DuplicateSet screening

The current cheap screening phenotype is intentionally simple and transparent:

- all instances have the same filename;
- exactly one instance occurs in each represented directory;
- at least three directories are represented.

Candidates are ordered by descending directory count, then filename. The screen uses only DuplicateSet data and therefore runs before DirectoryPair construction. It does not attempt to infer a Situation or automatically accept anything.

The user-facing question is whether all copies are intentionally distributed and should be retained. Accepting a candidate removes that Content from unresolved duplicate evidence before DirectoryPair and BranchPair construction, potentially reducing many downstream Cases through combinatorial effects.

The AeonHacs experiment screened 185 candidates and the user accepted 48. Compared with the immediately preceding no-settlement baseline, DirectoryPairs fell from 1,734 to 1,153, BranchPairs from 11,141 to 7,971, and total Cases from 13,623 to 9,824. The step therefore appears to provide both semantic cleanup and substantial structural reduction.

The interaction also exposed two further phenomena for study:

- repeated related files such as Git hook `.sample` files or DLL/PDB/XML groups may be more coherent as one higher-level repeated-structure Case than as many independent DuplicateSet Cases;
- opaque repository/application-managed files may be analytically useful evidence but poor deletion targets, reinforcing the distinction between duplicate identity and safe removability.

## Directory analysis

Directory records describe direct Files only. `FileCount` remains physical; duplicate-file and duplicate-Content counts describe unresolved duplicate evidence.

Each unresolved Content contributes once to a `DirectoryPair` when at least one unresolved File-instance relationship crosses the two directories. The DirectoryPair graph supplies degree, weighted degree, strongest-edge concentration, connected components, density, and related diagnostics.

## Branch statistics and analysis

`BranchStatisticsAnalyzer` derives settlement-aware local statistics for every duplicate-bearing Branch independently of BranchPair enumeration. Each `BranchRecord` contains:

    Path
    ParentPath
    FileCount
    DirectoryCount
    DuplicateFileCount
    DuplicateContentCount
    DuplicateDirectoryCount

`DuplicateContentCount` is a distinct-Content count across the whole branch, not the sum of descendant directory counts. These are fundamental exploratory values from which concentration and hierarchy gradients can be derived without first enumerating BranchPairs.

The report lists the top Branches by distinct duplicated Content and shows duplicate-file and duplicate-directory fractions plus parent-relative duplicated-Content, file, and directory retention. Branch-statistics analysis is timed separately.

`BranchPriorityMetrics` now derives experimental parent-relative seed measures:

    C = duplicated-Content retention / file retention
    D = distinct duplicated Content in the child Branch

and reports four rankings:

    C
    D * C
    D * ln(C), clamped to zero when C <= 1
    D * (1 - 1/C), clamped to zero when C <= 1

The last measure is bounded above by D and can be interpreted as duplicated Content concentrated beyond what would be expected from the Branch's share of parent files. No formula is preferred yet. The report prints the top 50 for each measure and sampled ranks through the full population to make knees or sharp falloffs visible.

BranchPair analysis remains separate and consumes the already constructed `DirectoryPair` graph instead of reconstructing pair evidence independently from DuplicateSets.

Each DirectoryPair edge is propagated through ancestor-or-self Branch combinations. Its leverage is added once to each effective BranchPair cut it crosses. The current BranchPair measure is therefore weighted cut size, and `DirectoryPairCount` records the number of distinct direct DirectoryPairs contributing to the cut.

Nested BranchPairs remain effective disjoint cuts: if one root is inside the other, the descendant subtree is excluded from the ancestor side. Moving a nested boundary can legitimately increase or decrease crossing evidence.

This eliminates duplicated DuplicateSet-to-directory-pair expansion and makes settlement effects flow naturally from DirectoryPairs into BranchPairs.

## Cases and ordering

`CaseAnalyzer` forms one population from unresolved DuplicateSet, Single-directory, DirectoryPair, and BranchPair Cases. It ranks lightweight candidates before materializing bounded File sets for the requested sample.

Descending leverage remains the temporary sampling order only. Real-corpus work has established that leverage is not the program objective and is not known to be the best general Case ordering. Exact distinct-Content BranchPair leverage and weighted structural leverage produce materially different rankings. Specificity, coverage, concentration, boundary position, and settlement impact remain active empirical characteristics.

The current research framing distinguishes two broad viewpoints:

- file-centric: why is this Content duplicated broadly or repeatedly?
- container-centric: why does this directory or Branch contain so much duplicated Content?

The working hypothesis is that cheap local statistics can generate promising Cases directly and reduce dependence on exhaustive pair enumeration. File-centric grouped settlement has already shown large downstream benefit; the current experiment asks whether concentrated container-centric Branch seeds can be ranked cheaply before counterpart search.

The actual objective is reduction of the user's remaining decision work. A Resolution can accomplish this through filesystem changes, acceptance settlements, or both. Resolving several objectively similar Cases with one compact user interaction is therefore directly aligned with the program objective.

## Structural diagnostics

The top-Case report retains full run context and timing. `StructuralEvidenceAnalyzer` derives sampled BranchPair evidence on demand, including per-side breadth, crossing-evidence directories, contributing DirectoryPairs, hierarchical relatives, boundary movement, and direct-evidence concentration.

Report-time structural analysis is separately instrumented because large corpora exposed expensive repeated scans. These diagnostics remain exploratory and are not part of the intended final interactive cost model.

## Tests

`Berries.Core.Tests` currently contains twenty-one tests.

Coverage includes Corpus normalization, Portrait construction and ignore filtering, duplicate discovery, file eviction, propagation of programming failures, settlement-aware directory analysis, first-class Branch statistics, parent-relative Branch priority metrics, graph metrics, DirectoryPair-driven BranchPair analysis including weighted evidence and nested cuts, settlement propagation through DirectoryPairs into BranchPairs, Case ranking/bounding, and structural evidence.

The settlement tests cover both whole-DuplicateSet acceptance and selective pairwise acceptance with other mates remaining unresolved.

## Immediate empirical work

Run the same non-source-code corpus with the existing ignore configuration and inspect the experimental seed rankings. We want to determine whether any of the four simple measures exhibits a useful sharp falloff and whether recognized large concentrated structures such as the LTspice branches rise naturally without the ranking being swamped by tiny high-ratio branches.

If one simple measure behaves well, use its highest-value Branches as seeds and search for counterpart Branches sharing the greatest amount of their duplicated Content. The purpose is to test whether useful BranchPair Cases can be discovered directly rather than exhaustively enumerated.

Case ordering remains an open research question. No Situation inference, persistent Resolution rules, generalization rules, Dispositions, virtual ActionPlans, or physical execution have yet been implemented.
