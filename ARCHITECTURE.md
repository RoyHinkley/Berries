# Berries Architecture

This document defines the architectural boundaries and runtime responsiveness rules that govern implementation placement. These are design constraints, not optional optimization guidelines.

## Dependency direction

Berries separates domain computation, presentation construction, platform filesystem access, and the Avalonia shell.

    Berries.Core
        domain/session model
        Corpus and Portrait acquisition
        Group discovery
        duplicate/structural analysis
        analysis lifecycle and scheduling
        portrait operations
        filesystem-action planning/execution contracts
        model queries

    Berries.Projection
        UI-independent construction of Explorer presentation models
        ProjectionState navigation/presentation state

    Berries.FileSystem.Abstractions
        platform-neutral filesystem boundary

    Berries.FileSystem.Windows
        Windows filesystem implementation

    Berries.Gui
        Avalonia controls and interaction handling
        GUI-specific presentation state and models
        ExplorerNode construction and control binding
        status/progress display

Core must not depend on Projection or Gui. Projection may depend on Core because it transforms Core state into presentation-shaped models. Gui may depend on Core and Projection but must not acquire domain-computation responsibilities merely because it initiated an operation.

## Computation placement

Placement is determined by **ownership and meaning**, not by computational cost or by whether the implementation is asynchronous.

> Put work at the lowest reusable layer that naturally owns it.

In practice:

- **Core** owns domain facts, analysis, model queries, and reusable domain computation.
- **Projection** owns UI-independent transformations whose result exists specifically to support a projection or presentation.
- **Gui** owns genuinely GUI-specific state and construction, including `ExplorerNode` creation and Avalonia binding.

Examples of Core work include scanning, hashing, Group construction, unique-file accounting, Portrait reconstruction, structural analysis, relationship scoring, and factual model queries.

Examples of warranted Projection work include building UI-independent Explorer hierarchy models, presentation labels, presentation ordering, and other transformations whose result exists specifically to support a projection.

Examples of warranted Gui work include constructing `ExplorerNode` objects, maintaining partially constructed visible trees, synchronizing visual selection, and assigning or incrementally updating Avalonia controls.

Moving a large loop to `Task.Run` does not change which layer owns the work. Conversely, work does not belong in Core or Projection merely because it is expensive. If substantial work is inherently GUI work, it remains in Gui and must satisfy the responsiveness contract there.

## Responsiveness contract

Architectural ownership and responsiveness are orthogonal concerns.

The Avalonia UI thread must remain responsive while Berries performs work whose runtime can grow materially with Corpus, Portrait, projection, or visible-tree size.

Any operation that may take appreciable time must therefore, wherever it naturally belongs:

1. execute asynchronously without monopolizing the GUI thread;
2. accept or otherwise participate in a `CancellationToken`-based cancellation lifetime;
3. check cancellation within the loop whose work scales with the data size, with sufficiently fine granularity that cancellation remains responsive;
4. report a meaningful user-facing phase description;
5. report `Completed` and `Total` whenever the total work can be determined cheaply enough that counting it does not itself become disproportionate work.

A merely asynchronous wrapper is insufficient if an expensive inner traversal or sort remains effectively uncancellable.

Where useful, GUI presentation work may publish completed portions incrementally rather than withholding the entire result until construction is complete. Incremental publication should use bounded batches so that control realization and layout do not themselves monopolize the UI thread.

### Determinate progress is preferred

When the work population is already known or cheaply countable, progress should be determinate. Typical examples include:

    files in an acquired Portrait
    candidate files to hash
    Groups to inspect
    files within known Groups
    known Directory/Branch records
    known candidate relationships
    Explorer nodes in a known projection

The status bar should show both the phase and count, for example:

    Constructing Groups — 12,400 / 18,250

### Indeterminate progress is acceptable when necessary

Some operations do not know their total cheaply in advance. Filesystem enumeration is the principal example: determining the total may require essentially performing the enumeration twice.

In those cases Berries should still report a meaningful phase and useful observed counts where available, while the progress bar remains indeterminate.

Indeterminate progress should be a consequence of genuinely unknowable or disproportionately expensive totals, not convenience in the implementation.

## Progress ownership

The layer performing an operation reports progress as data. Core and Projection never manipulate GUI controls; GUI-specific work may report its own progress directly through the same status/progress presentation mechanism.

The GUI owns presentation of progress in the status bar. A progress report should contain enough information for the GUI to choose determinate or indeterminate display without understanding or reimplementing lower-layer computation.

The layer performing the work owns the wording of the computational phase because it knows what work is actually occurring. The GUI may add surrounding interaction context, but it should not hard-code a misleading description of a multi-phase lower-layer operation.

## Cancellation and correctness

Cancellation is required for responsiveness and efficiency, but cancellation is not generally the correctness mechanism for stale asynchronous work.

Derived analysis is computed against a stable Portrait generation. When the Working Portrait changes:

- the previous generation becomes stale immediately;
- obsolete work is asked to cancel;
- stale results are prevented from publishing by generation validation;
- current-generation analysis is scheduled.

Thus prompt cancellation avoids wasted computation, while generation validation prevents stale results from becoming authoritative.

Explorer navigation follows the same principle at the GUI level:

- each navigation request receives an operation generation and cancellation token;
- beginning a newer navigation cancels the previous navigation for efficiency;
- only the current navigation generation has authority to publish or replace visible Explorer state;
- an obsolete navigation that finishes late cannot overwrite the newer requested view.

Therefore navigation ownership provides correctness while cancellation provides responsiveness.

## Application orchestration

`BerriesApplication` owns application-level sequencing that crosses Core computations: session replacement, serialized portrait mutation, portrait generation, and dependency-driven analysis scheduling.

The GUI does not maintain a second analysis scheduler, cancellation lifecycle, or refresh queue. It observes Core progress and product publication and updates capabilities accordingly.

GUI navigation lifetime is distinct from analysis scheduling and is correctly owned by Gui because it governs which user-requested presentation may publish to Explorer controls.

## Projection boundary

Projection is not a general-purpose place to move expensive work out of the GUI. Computation belongs in Projection only when its result is itself UI-independent and presentation-shaped.

For example:

- determining which Groups exist is Core;
- determining which Branches share content is Core;
- building a `GroupProjection` label and presentation ordering is Projection;
- constructing a UI-independent Branch Explorer hierarchy from already-established factual placements is Projection;
- creating `ExplorerNode` objects and assigning or incrementally updating Avalonia `TreeView` content is Gui.

This distinction keeps semantic computation reusable and testable independently of any current Explorer presentation without forcing legitimate GUI work into a lower layer merely to obtain asynchronous execution.

## Review rule

When adding or changing work whose runtime scales with user data, review it explicitly for:

    correct architectural owner
    asynchronous/non-blocking execution where appreciable
    cancellation granularity
    meaningful phase reporting
    determinate progress where practical
    bounded GUI-thread publication/realization
    stale-result publication protection where operations can overlap

A responsiveness regression is an architectural regression even if the result is functionally correct.
