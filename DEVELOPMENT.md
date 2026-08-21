# Development

The governing design is split across `PROJECT.md`, `MODEL.md`, `ANALYSIS.md`, `SITUATIONS.md`, and `WORKFLOW.md`. `PROJECT.md` is the short architectural overview and index. This file records the current implementation state and immediate empirical work.

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

1. construct the Initial Portrait;
2. discover DuplicateSets by size grouping and SHA-256 hashing;
3. cheaply screen for same-name DuplicateSets occurring once each in at least three distinct directories;
4. present those candidates as a checklist asking which should be accepted with the retain-all resolution;
5. apply the checked whole-DuplicateSet settlements;
6. analyze unresolved direct-directory relationships;
7. construct ScopePairs from the resulting DirectoryPair graph;
8. rank/materialize the top Case sample;
9. build the structural report and append the early-settlement selection summary.

The old individual `Find duplicates`, `Analyze directories`, `Analyze scopes`, and `Top cases` GUI buttons have been removed. Their controller operations remain separate so analysis stages stay testable and reusable.

The checklist is deliberately an exploratory pre-structural decision pass. Nothing is preselected. Canceling the dialog aborts the remainder of the run. Checked items are settlements for the current run only; persistent filename rules have not yet been implemented.

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

The user-facing question is whether all copies are intentionally distributed and should be retained. Accepting a candidate removes that Content from unresolved duplicate evidence before DirectoryPair and ScopePair construction, potentially reducing many downstream Cases through combinatorial effects.

The current purpose is empirical: compare this settlement-aware run against the prior baseline on the same corpus and determine whether resolving several such DuplicateSet Cases in one short questionnaire produces substantial aggregate reduction in DirectoryPairs, ScopePairs, and total Cases.

## Directory analysis

Directory records describe direct Files only. `FileCount` remains physical; duplicate-file and duplicate-Content counts describe unresolved duplicate evidence.

Each unresolved Content contributes once to a `DirectoryPair` when at least one unresolved File-instance relationship crosses the two directories. The DirectoryPair graph supplies degree, weighted degree, strongest-edge concentration, connected components, density, and related diagnostics.

## Scope analysis

Scope analysis consumes the already constructed `DirectoryPair` graph instead of reconstructing pair evidence independently from DuplicateSets.

Each DirectoryPair edge is propagated through ancestor-or-self Scope combinations. Its leverage is added once to each effective ScopePair cut it crosses. The current ScopePair measure is therefore weighted cut size, and `DirectoryPairCount` records the number of distinct direct DirectoryPairs contributing to the cut.

Nested ScopePairs remain effective disjoint cuts: if one root is inside the other, the descendant subtree is excluded from the ancestor side. Moving a nested boundary can legitimately increase or decrease crossing evidence.

This eliminates duplicated DuplicateSet-to-directory-pair expansion and makes settlement effects flow naturally from DirectoryPairs into ScopePairs.

## Cases and ordering

`CaseAnalyzer` forms one population from unresolved DuplicateSet, Single-directory, DirectoryPair, and ScopePair Cases. It ranks lightweight candidates before materializing bounded File sets for the requested sample.

Descending leverage remains the temporary sampling order only. Real-corpus work has established that leverage is not the program objective and is not known to be the best general Case ordering. Exact distinct-Content ScopePair leverage and weighted structural leverage produce materially different rankings. Specificity, coverage, concentration, boundary position, and settlement impact remain active empirical characteristics.

The actual objective is reduction of the user's remaining decision work. A Resolution can accomplish this through filesystem changes, acceptance settlements, or both. Resolving several objectively similar Cases with one compact user interaction is therefore directly aligned with the program objective.

## Structural diagnostics

The top-Case report retains full run context and timing. `StructuralEvidenceAnalyzer` derives sampled ScopePair evidence on demand, including per-side breadth, crossing-evidence directories, contributing DirectoryPairs, hierarchical relatives, boundary movement, and direct-evidence concentration.

Report-time structural analysis is separately instrumented because large corpora exposed expensive repeated scans. These diagnostics remain exploratory and are not part of the intended final interactive cost model.

## Tests

`Berries.Core.Tests` currently contains seventeen tests.

Coverage includes Corpus normalization, Portrait construction, duplicate discovery, file eviction, propagation of programming failures, settlement-aware directory analysis, graph metrics, DirectoryPair-driven ScopePair analysis including weighted evidence and nested cuts, settlement propagation through DirectoryPairs into ScopePairs, Case ranking/bounding, and structural evidence.

The settlement tests cover both whole-DuplicateSet acceptance and selective pairwise acceptance with other mates remaining unresolved.

## Immediate empirical work

The next AeonHacs run should use the new checklist before structural analysis. Compare its resulting DirectoryPair, ScopePair, and total Case counts against the established no-settlement baseline from the immediately preceding run. The accepted filenames/folder counts are appended to the report so the aggregate result can be interpreted against the actual user decisions.

Case ordering remains an open research question. No Situation inference, persistent Resolution rules, generalization rules, Dispositions, virtual ActionPlans, or physical execution have yet been implemented.
