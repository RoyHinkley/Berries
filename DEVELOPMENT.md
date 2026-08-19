# Development

The governing design is split across `PROJECT.md`, `MODEL.md`, `ANALYSIS.md`, `SITUATIONS.md`, and `WORKFLOW.md`. `PROJECT.md` is the short architectural overview and index; the other documents contain the focused design detail.

The solution follows the decomposition in `PROJECT.md`:

- `Berries.Core` — platform/UI-independent domain, analysis, Cases, decisions, logical planning, and execution-plan contracts.
- `Berries.FileSystem.Abstractions` — the deliberately small platform-neutral filesystem boundary.
- `Berries.FileSystem.Windows` — Windows filesystem adapter.
- `Berries.Gui` — Avalonia desktop front end and GUI-specific controller/state.
- `Berries.Core.Tests` — synthetic, platform-independent tests of Core behavior and boundaries.

There is deliberately no console front end. Independence of Core is an architectural requirement, not a requirement to maintain a second front end. `Berries.Core.Tests` serves as the executable architectural test: Core can be exercised without Avalonia or a platform filesystem implementation.

Target framework is .NET 10. The GUI references Avalonia 12.1.0 and is built as `WinExe` so the Windows GUI does not create a console window.

## Current implementation

The working vertical slices now cover Corpus construction, Initial Portrait acquisition, duplicate discovery, direct-directory structural analysis, ScopePair analysis, unified Case ranking/sampling, and exploratory structural diagnostics.

1. The GUI maintains a persistent list of Corpus roots. `Add` uses a single-directory picker; `Remove` removes the selected root.
2. `BerriesEngine.CreateCorpus` normalizes selected paths before enumeration: paths are canonicalized, exact duplicates are removed, and roots contained by another selected root are discarded. The stored `Corpus` therefore contains the minimal disjoint root set.
3. `GuiController` awaits `BerriesEngine.BuildInitialPortraitAsync` to acquire filesystem state and construct the Initial Portrait.
4. `BerriesEngine` owns worker-thread boundaries for potentially long-running operations and supports cancellation; filesystem enumeration itself remains synchronous because that is what the platform exposes.
5. Core obtains filesystem state only through `IFileSystem`; the abstraction describes required filesystem capabilities without prescribing platform implementation strategy.
6. `WindowsFileSystem` recursively enumerates regular Files while avoiding reparse-point traversal.
7. Portrait construction from acquired file records remains synchronous and deterministic.
8. Duplicate discovery first groups Portrait Files by length. Only Files in non-singleton length groups are opened and hashed.
9. Candidate Files are hashed with SHA-256. Files sharing a hash are partitioned into `DuplicateSet` instances; singleton hashes are discarded.
10. Directory analysis derives `DirectoryRecord`s only for directories directly containing duplicated Content. Each record contains direct File count, duplicated-File count, and distinct duplicated-Content count; descendants are not folded into these statistics.
11. For each `DuplicateSet`, every unordered pair of distinct directories directly representing that Content contributes one unit of leverage to a `DirectoryPair`. Multiple instances of the same Content within one directory do not increase pair leverage.
12. `DirectoryPair`s are ordered by descending leverage. The temporary GUI displays counts, phase timing, and the 25 strongest pairs.
13. Scope analysis walks directory ancestry only through the filesystem abstraction; Core does not parse platform path syntax. `IFileSystem.GetParentDirectory` supplies the hierarchy operation required.
14. Each direct duplicated-Content relationship contributes evidence to containing pairs of directory-rooted Scopes. ScopePair leverage is the exact number of distinct duplicated Contents crossing the two effective sides; `DirectoryPairCount` records the number of distinct direct DirectoryPairs supplying evidence.
15. ScopePair sides are always effectively disjoint. If one root is a descendant of the other, the descendant subtree is omitted from the ancestor side before evidence is counted. Identical roots are never a ScopePair.
16. ScopePairs are ordered by descending leverage, then descending contributing DirectoryPair count. The temporary GUI displays counts, phase timing, and the 25 strongest pairs alongside the strongest direct DirectoryPairs.
17. `CaseAnalyzer` forms one objective population from DuplicateSet, internally duplicated single-directory, DirectoryPair, and ScopePair Cases. All four use the common `Case.Leverage` metric.
18. Case ranking is deliberately lightweight: candidates are currently ranked by leverage before bounded File sets are materialized. Only the requested sample (currently the top 25) is materialized. Candidate construction, ranking, and materialization are now timed separately because very large real corpora exposed substantial end-to-end delay.
19. Structural Case bounds include unique Files as defined in `MODEL.md` and `ANALYSIS.md`: a single-directory Case contains all direct Files in that directory; a DirectoryPair Case contains all direct Files in both directories; a ScopePair Case contains all Files on its two effective disjoint sides.
20. Directory analysis also treats the DirectoryPair population as a simple weighted undirected graph for exploratory diagnostics. It records total directories, duplicate-bearing directories, internally duplicated directories, pair-participating directories, connected components, largest component, pair density, and per-directory degree, weighted degree, maximum incident-pair leverage, mean incident-pair leverage, and strongest-pair concentration.
21. Pair overlap characteristics are derived cheaply from existing statistics. For a DirectoryPair the report shows directional shared-Content coverage, Jaccard overlap, endpoint degrees, and the fraction of each endpoint's weighted degree represented by that edge.
22. `StructuralEvidenceAnalyzer` derives evidence only for sampled ScopePairs rather than materializing another large persistent graph. It now records effective-side directory/File breadth separately for each side, the number of directories on each side participating in crossing DirectoryPairs, directional coverage and asymmetry, direct-evidence concentration, root-depth movement for strong hierarchical relatives, and counts of related ScopePairs retaining at least 90%, 95%, and 99% of the reference leverage.
23. The current exploratory code calls a hierarchically related ScopePair "subsidiary" when its roots lie within corresponding effective-side hierarchy positions. Real-data study has shown that this is **not** a strict refinement/subset relation for nested ScopePairs: moving a descendant root moves the effective-side cut and can increase or decrease leverage. The report's leverage ratio should therefore be interpreted as a relationship metric, not guaranteed monotonic retention.
24. The temporary top-Case report is a single selectable text view. It now begins with full run context: Corpus roots, scan/current Portrait sizes, duplicate counts/evictions, DirectoryPair and ScopePair populations, complete phase timings, leverage distributions for all four Case types, and graph summary. DuplicateSet Cases still show their instances; structural Cases emphasize structural statistics and relationships rather than long duplicated-file samples.
25. Report-time structural analysis is instrumented separately. For sampled ScopePairs it records time spent scanning contributing DirectoryPairs, scanning related ScopePairs, counting duplicated Content on effective sides, computing parent breadth, and computing breadth for the displayed related ScopePairs, plus total report-generation time.
26. The structural-evidence implementation retains only the strongest requested related ScopePairs while scanning rather than sorting/materializing the entire related population. This removes an obvious large-corpus cost without changing the reported result.

Phase timing is development instrumentation. Portrait acquisition reports scan time. Duplicate discovery separately measures size grouping, Content hashing, DuplicateSet construction, and total elapsed time. Directory analysis separately measures DirectoryRecord construction, DirectoryPair construction, and total elapsed time; graph metrics are intentionally inexpensive and included within that analysis. Scope analysis separately measures direct evidence construction, scope aggregation, result construction, and total elapsed time. Case analysis now separates candidate construction, ranking, and materialization. The report prints a measured pipeline subtotal before report generation and then reports its own timing breakdown.

The filesystem abstraction may eventually warrant performance-oriented refinement. Platform adapters should remain free to obtain metadata efficiently in bulk or during enumeration, and Core should not require metadata it does not use. Optimization remains evidence-driven; the current timing expansion is intended to identify the actual cost centers before broader changes are made.

## File accessibility policy

Every File in the Current Portrait is a File Berries still considers safely actionable.

When a filesystem operation on a particular File fails because it is unavailable or inaccessible (`IOException`, `UnauthorizedAccessException`, or `SecurityException`), that File is evicted from the Current Portrait for the remainder of the session. Subsequent analysis acts as though it no longer exists. The failed operation and reason are retained as a `FileEviction` diagnostic record.

Programming and other non-filesystem failures are not converted into file evictions; they continue to propagate normally so defects are not hidden.

On Windows, Content reads request permissive sharing (`FileShare.ReadWrite | FileShare.Delete`) so Berries can coexist with other processes when Windows permits it. If an existing handle still prevents access, the File is evicted rather than causing analysis to fail.

Portraits remain immutable snapshots. An operation that evicts Files returns a replacement Current Portrait rather than modifying the input Portrait in place.

## Tests

`Berries.Core.Tests` currently contains thirteen tests.

The tests exercise Core against synthetic filesystem data, including asynchronous Portrait construction, Corpus-root normalization, duplicate discovery, file eviction on I/O failure, propagation of programming failures, direct directory analysis, ScopePair analysis, Case ranking/bounding, graph metrics, and structural evidence.

Directory-analysis coverage verifies graph counts, connectivity, density, degree, weighted degree, maximum incident leverage, mean incident leverage, and strongest-pair concentration. Structural-evidence coverage verifies contributing DirectoryPair selection, hierarchical ScopePair detection, root nesting, per-side duplicated-Content counts, per-side breadth, crossing-evidence directories, root-depth movement, evidence concentration, and the effective-side exclusion rule for nested ScopePairs.

## Current empirical-development step

No Situation inference or characteristic classifier has been implemented. The present goal is to inspect structurally informative samples from real corpora and discover which objective characteristics actually separate recognizable filesystem Situations.

Real-data inspection has established several important points now recorded in `MODEL.md` and `ANALYSIS.md`:

- Leverage is the payoff represented by the Case's defining duplication pattern, not all duplication enclosed by its bounds.
- A Case need not resolve unrelated internal duplication.
- ScopePair leverage is duplicated-Content connectivity crossing the effective-side cut.
- Moving a nested ScopePair boundary can legitimately increase or decrease leverage because it repartitions the tree.
- Leverage alone is not presentation priority; coverage, concentration, specificity/breadth, directional containment, and structural position are useful objective dimensions.
- Total ScopePair breadth is insufficient along nested cuts because moving the cut can repartition the same containing scope without changing the union of the effective sides. Side-specific and crossing-evidence breadth are therefore now collected.
- Hierarchical ScopePair ancestry is useful evidence but is not automatically a monotonic refinement ordering.

The working heuristic remains: ask the smallest comprehensible question with the greatest downstream simplifying effect. Multi-objective/Pareto-style comparison remains the preferred next ranking experiment rather than an arbitrary weighted scalar. Current empirical work is intended to determine which objective measures genuinely express specificity before any deterministic priority relation is adopted.

A Case being structurally characterized does not mean its Situation has been determined. Eventually, characteristic predicates may mark regions of the Case population as empirically covered so subsequent samples expose new structural phenotypes. If objective structure does not narrow a Case semantically, that is acceptable: the user supplies the Situation.

## Not yet implemented

Multi-objective Case ordering, empirical characteristic coverage, Situation/Resolution mapping, Dispositions, virtual ActionPlans/Portrait transformation, execution planning, and physical filesystem execution remain future work described by the governing design documents.
