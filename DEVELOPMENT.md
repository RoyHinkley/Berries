# Development

The solution follows the decomposition in `PROJECT.md`:

- `Berries.Core` — platform/UI-independent domain, analysis, cases, decisions, logical planning, and execution-plan contracts.
- `Berries.FileSystem.Abstractions` — the deliberately small platform-neutral filesystem boundary.
- `Berries.FileSystem.Windows` — Windows/NTFS adapter.
- `Berries.Gui` — Avalonia desktop front end and GUI-specific controller/state.
- `Berries.Core.Tests` — synthetic, platform-independent tests of Core behavior and boundaries.

`Berries.Console` has been removed. Independence of Core is an architectural requirement, not a requirement to maintain a second front end. `Berries.Core.Tests` now serves as the executable architectural test: Core can be exercised without Avalonia or a platform filesystem implementation.

The first implemented vertical slice is intentionally small:

1. The GUI selects one corpus root.
2. `GuiController` awaits `BerriesEngine.BuildInitialPortraitAsync`.
3. `BerriesEngine` owns the worker-thread boundary and supports cancellation and platform-neutral progress reporting.
4. Core obtains filesystem state only through synchronous `IFileSystem`; the abstraction mirrors the filesystem capability rather than pretending directory enumeration is asynchronous.
5. `WindowsFileSystem` recursively enumerates regular files while avoiding reparse-point traversal.
6. Portrait construction from the acquired file records remains synchronous.
7. The GUI displays the root, file count, total bytes, and scan time.

No duplicate detection, hashing, structural analysis, cases, situations, dispositions, action planning, or execution behavior is implemented yet.

Target framework is .NET 10. The GUI references Avalonia 12.1.0.

The first Core test asynchronously builds a portrait from a synthetic `IFileSystem`. All filesystem operations other than enumeration deliberately throw, so the test also verifies that initial portrait construction depends only on the filesystem capability it actually needs.


## Corpus roots

A corpus may contain multiple filesystem roots. User selections are normalized before enumeration: paths are canonicalized, exact duplicates are removed, and any root contained by another selected root is discarded. The stored `Corpus` therefore contains the minimal disjoint root set.
