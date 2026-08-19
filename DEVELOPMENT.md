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

The working vertical slices now cover corpus construction, initial portrait acquisition, duplicate discovery, direct-directory structural analysis, and ScopePair analysis.

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

Phase timing is included as development instrumentation. Portrait acquisition reports scan time. Duplicate discovery separately measures size grouping, content hashing, duplicate-set construction, and total elapsed time. Directory analysis separately measures directory-record construction, DirectoryPair construction, and total elapsed time. Scope analysis separately measures direct evidence construction, scope aggregation, result construction, and total elapsed time. These measurements are intended to guide later filesystem/performance work; correctness takes precedence over premature optimization.

The filesystem abstraction may eventually warrant performance-oriented refinement. In particular, platform adapters should remain free to obtain metadata efficiently in bulk or during enumeration, and Core should not require metadata it does not actually use. No optimization is justified yet without measurements showing a material cost.

## File accessibility policy

Every file in the current Portrait is a file Berries still considers safely actionable.

When a filesystem operation on a particular file fails because the file is unavailable or inaccessible (`IOException`, `UnauthorizedAccessException`, or `SecurityException`), that file is evicted from the current Portrait for the remainder of the session. Subsequent analysis acts as though the file no longer exists. The failed operation and reason are retained as a `FileEviction` diagnostic record.

Programming and other non-filesystem failures are not converted into file evictions; they continue to propagate normally so defects are not hidden.

On Windows, content reads request permissive sharing (`FileShare.ReadWrite | FileShare.Delete`) so Berries can coexist with other processes when Windows permits it. If an existing handle still prevents access, the file is evicted rather than causing the analysis to fail.

Portraits remain immutable snapshots. An operation that evicts files returns a replacement current Portrait rather than modifying the input Portrait in place.

## Tests

`Berries.Core.Tests` should contain eight tests after this revision: the six previously passing tests plus two ScopePair tests.

The existing tests exercise Core against synthetic filesystem data, including asynchronous portrait construction, corpus-root normalization, duplicate discovery, file eviction on I/O failure, propagation of programming failures, and direct directory analysis.

The ScopePair tests verify two governing properties: descendant DirectoryPairs aggregate into higher-level scope leverage by distinct content, and when scope roots are nested, duplicated content wholly inside the descendant subtree does not leak into the ancestor side. Scope analysis requires path hierarchy semantics but performs no file I/O.

## Not yet implemented

Case discovery and ranking, Situations and Resolutions, Dispositions, virtual Action Plans/Portrait transformation, execution planning, and physical filesystem execution remain future work described in `PROJECT.md`.
