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

The first working vertical slices now cover corpus construction, initial portrait acquisition, and duplicate discovery.

1. The GUI maintains a persistent list of corpus roots. `Add` uses a single-directory picker; `Remove` removes the selected root.
2. `BerriesEngine.CreateCorpus` normalizes selected paths before enumeration: paths are canonicalized, exact duplicates are removed, and roots contained by another selected root are discarded. The stored `Corpus` therefore contains the minimal disjoint root set.
3. `GuiController` awaits `BerriesEngine.BuildInitialPortraitAsync` to acquire filesystem state and construct the initial portrait.
4. `BerriesEngine` owns the worker-thread boundary and supports cancellation and platform-neutral progress reporting.
5. Core obtains filesystem state only through synchronous `IFileSystem`; the abstraction describes required filesystem capabilities without prescribing platform implementation strategy.
6. `WindowsFileSystem` recursively enumerates regular files while avoiding reparse-point traversal.
7. Portrait construction from the acquired file records remains synchronous.
8. Duplicate discovery first groups portrait files by length. Only files in non-singleton length groups are opened and hashed.
9. Candidate files are hashed with SHA-256. Files sharing a hash are partitioned into `DuplicateSet` instances; singleton hashes are discarded.
10. The GUI keeps portrait acquisition and duplicate discovery as separate operations so their performance can be observed independently.

Phase timing is included as development instrumentation. Portrait acquisition reports scan time. Duplicate discovery separately measures size grouping, content hashing, duplicate-set construction, and total elapsed time. These measurements are intended to guide later filesystem/performance work; correctness takes precedence over premature optimization.

The filesystem abstraction may eventually warrant performance-oriented refinement. In particular, platform adapters should remain free to obtain metadata efficiently in bulk or during enumeration, and Core should not require metadata it does not actually use. No optimization is justified yet without measurements showing a material cost.

## Tests

`Berries.Core.Tests` currently contains three passing tests.

The tests exercise Core against synthetic filesystem data, including asynchronous portrait construction, corpus-root normalization, and duplicate discovery. Duplicate-discovery coverage verifies that equal-content files form a duplicate set, same-size files with different content do not, and uniquely-sized files are never opened for hashing.

## Not yet implemented

Structural directory/pair/scope analysis, Case discovery and ranking, Situations and Resolutions, Dispositions, virtual Action Plans/Portrait transformation, execution planning, and physical filesystem execution remain future work described in `PROJECT.md`.
