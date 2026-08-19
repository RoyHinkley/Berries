# Development

The solution follows the decomposition in `PROJECT.md`:

- `Berries.Core` — platform/UI-independent domain, analysis, cases, decisions, logical planning, and execution-plan contracts.
- `Berries.FileSystem.Abstractions` — the deliberately small platform-neutral filesystem boundary.
- `Berries.FileSystem.Windows` — Windows/NTFS adapter.
- `Berries.Gui` — Avalonia desktop front end and GUI-specific controller/state.
- `Berries.Core.Tests` — synthetic, platform-independent tests of Core behavior and boundaries.

There is deliberately no console front end. Independence of Core is an architectural requirement, not a requirement to maintain a second front end. `Berries.Core.Tests` serves as the executable architectural test: Core can be exercised without Avalonia or a platform filesystem implementation.

Target framework is .NET 10. The GUI references Avalonia 12.1.0 and is built as `WinExe` so the Windows GUI does not create a console window.

## Current implementation

The working vertical slices now cover corpus construction, initial portrait acquisition, duplicate discovery, direct-directory structural analysis, ScopePair analysis, unified Case ranking/sampling, and exploratory structural diagnostics.

1. The GUI maintains a persistent list of corpus roots. `Add` uses a single-directory picker; `Remove` removes the selected root.
2. `BerriesEngine.CreateCorpus` normalizes selected paths before enumeration: paths are canonicalized, exact duplicates are removed, and roots contained by another selected root are discarded. The stored `Corpus` therefore contains the minimal disjoint root set.
3. `GuiController` awaits `BerriesEngine.BuildInitialPortraitAsync` to acquire filesystem state and construct the initial portrait.
4. `BerriesEngine` owns worker-thread boundaries for potentially long-running operations and supports cancellation; filesystem enumeration itself remains synchronous because that is what the platform exposes.
5. Core obtains filesystem state only through `IFileSystem`; the abstraction describes required filesystem capabilities without prescribing platform implementation strategy.
6. `WindowsFileSystem` recursively enumerates regular files while avoiding reparse-point traversal.
7. Portrait construction from acquired file records remains synchronous and deterministic.
8. Duplicate discovery first groups portrait files by length. Only files in non-singleton length groups are opened and hashed.
9. Candidate files are hashed with SHA-256. Files sharing a hash are partitioned into `DuplicateSet` instances; singleton hashes are discarded.
10. Directory analysis derives `DirectoryRecord`s only for directories directly containing duplicated content. Each record contains direct file count, duplicated-file count, and distinct duplicated-content count; descendants are not folded into these statistics.
11. For each `DuplicateSet`, every unordered pair of distinct directories directly representing that content contributes one unit of leverage to a `DirectoryPair`. Multiple instances of the same content within one directory do not increase pair leverage.
12. `DirectoryPair`s are ordered by descending leverage. The temporary GUI displays counts, phase timing, and the 25 strongest pairs.
13. Scope analysis walks directory ancestry only through the filesystem abstraction; Core does not parse platform path syntax. `IFileSystem.GetParentDirectory` supplies the one hierarchy operation required.
14. Each direct duplicated-content relationship contributes evidence to every containing pair of directory-rooted scopes within the corpus. ScopePair leverage is the number of distinct duplicated contents crossing the two effective sides; `DirectoryPairCount` records the number of distinct direct DirectoryPairs supplying that evidence.
15. ScopePair sides are always effectively disjoint. If one scope root is a descendant of the other, the descendant subtree is omitted from the ancestor side before evidence is counted. Identical roots are never a ScopePair.
16. ScopePairs are ordered by descending leverage, then descending contributing DirectoryPair count. The temporary GUI displays counts, phase timing, and the 25 strongest pairs alongside the strongest direct DirectoryPairs.
17. `CaseAnalyzer` forms one objective population from DuplicateSet, internally duplicated single-directory, DirectoryPair, and ScopePair cases. All four use the common `Case.Leverage` metric.
18. Case ranking is deliberately lightweight: all candidate cases are ranked by leverage before their bounded file sets are materialized. Only the requested sample (currently the top 25) is materialized, avoiding a potentially enormous duplication of portrait membership across thousands of overlapping ScopePairs.
19. Structural case bounds include unique files as required by `PROJECT.md`: a single-directory case contains all direct files in that directory; a DirectoryPair case contains all direct files in both directories; a ScopePair case contains all files on its two effective disjoint sides.
20. Directory analysis now also treats the DirectoryPair population as a simple weighted undirected graph for exploratory diagnostics. It records total directories, duplicate-bearing directories, internally duplicated directories, pair-participating directories, connected components, largest component, pair density, and per-directory degree, weighted degree, and maximum incident-pair leverage.
21. Pair overlap characteristics are derived cheaply from existing statistics. For a DirectoryPair the report shows each side's shared-content coverage, Jaccard overlap, and endpoint degrees in addition to leverage.
22. `StructuralEvidenceAnalyzer` derives evidence only for sampled ScopePairs rather than materializing another large persistent graph. It identifies whether the two roots are nested, counts duplicated contents represented on each effective side, derives cross-side coverage, finds all subsidiary ScopePairs, and reports the strongest subsidiary ScopePairs and strongest contributing DirectoryPairs.
23. A ScopePair is considered subsidiary when its two roots lie within the corresponding effective sides of another ScopePair (in either orientation) and it is not the same unordered pair. The current exploratory report lists the strongest subsidiaries, not only immediate children; that distinction can be refined later if the real data makes it useful.
24. The temporary top-case report is a single selectable text view. DuplicateSet cases still show their instances; structural cases emphasize structural statistics and relationships rather than long duplicated-file samples.

Phase timing is included as development instrumentation. Portrait acquisition reports scan time. Duplicate discovery separately measures size grouping, content hashing, duplicate-set construction, and total elapsed time. Directory analysis separately measures directory-record construction, DirectoryPair construction, and total elapsed time; the graph metrics are intentionally inexpensive and included within that analysis. Scope analysis separately measures direct evidence construction, scope aggregation, result construction, and total elapsed time. These measurements are intended to guide later filesystem/performance work; correctness takes precedence over premature optimization.

The filesystem abstraction may eventually warrant performance-oriented refinement. In particular, platform adapters should remain free to obtain metadata efficiently in bulk or during enumeration, and Core should not require metadata it does not actually use. No optimization is justified yet without measurements showing a material cost.

## File accessibility policy

Every file in the current Portrait is a file Berries still considers safely actionable.

When a filesystem operation on a particular file fails because the file is unavailable or inaccessible (`IOException`, `UnauthorizedAccessException`, or `SecurityException`), that file is evicted from the current Portrait for the remainder of the session. Subsequent analysis acts as though the file no longer exists. The failed operation and reason are retained as a `FileEviction` diagnostic record.

Programming and other non-filesystem failures are not converted into file evictions; they continue to propagate normally so defects are not hidden.

On Windows, content reads request permissive sharing (`FileShare.ReadWrite | FileShare.Delete`) so Berries can coexist with other processes when Windows permits it. If an existing handle still prevents access, the file is evicted rather than causing the analysis to fail.

Portraits remain immutable snapshots. An operation that evicts files returns a replacement current Portrait rather than modifying the input Portrait in place.

## Tests

`Berries.Core.Tests` should contain thirteen tests after this revision: the previous eleven plus two structural-evidence tests.

The existing tests exercise Core against synthetic filesystem data, including asynchronous portrait construction, corpus-root normalization, duplicate discovery, file eviction on I/O failure, propagation of programming failures, direct directory analysis, ScopePair analysis, and Case ranking/bounding.

Directory-analysis coverage now also verifies graph counts, connectivity, density, degree, weighted degree, and maximum incident leverage. The structural-evidence tests verify contributing DirectoryPair selection, subsidiary ScopePair detection, root nesting, per-side duplicated-content counts, and the effective-side exclusion rule for nested ScopePairs.

## Current empirical-development step

No Situation inference or characteristic classifier has been implemented. The present goal is to inspect successive high-leverage samples from real corpora. We are deliberately adding inexpensive objective discriminators first—especially graph connectivity, pair overlap, scope ancestry, subsidiary structure, and evidence concentration—to discover which characteristics actually separate recognizable filesystem situations.

A case being structurally characterized does not mean its Situation has been determined. Eventually, characteristic predicates may mark regions of the case population as empirically covered so the next highest-leverage unexplored cases can be sampled. If objective structure does not narrow a case semantically, that is an acceptable result: the user must supply the Situation.

## Not yet implemented

Empirical characteristic coverage, Situation/Resolution mapping, Dispositions, virtual Action Plans/Portrait transformation, execution planning, and physical filesystem execution remain future work described in `PROJECT.md`.
