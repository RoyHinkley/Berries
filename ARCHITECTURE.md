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
        Avalonia controls, interaction handling, status/progress display,
        and assignment of completed presentation models to controls

Core must not depend on Projection or Gui. Projection may depend on Core because it transforms Core state into presentation-shaped models. Gui may depend on Core and Projection but must not acquire domain-computation responsibilities merely because it initiated an operation.

## Computation placement

The default placement rule is:

> Put computation in **Core whenever possible**. Put it in **Projection only when the computation is inherently projection/presentation work**. Do not put Corpus-, Portrait-, Group-, Directory-, or Branch-scale computation in the GUI.

Examples of Core work include scanning, hashing, Group construction, unique-file accounting, Portrait reconstruction, structural analysis, relationship scoring, and factual model queries.

Examples of warranted Projection work include building Explorer hierarchy models, presentation labels, presentation ordering, and other transformations whose result exists specifically to support a projection.

The GUI should perform bounded presentation work only: collect user intent, initiate an asynchronous Core/Projection operation, display its progress, then bind or assign the completed result to controls.

Moving a large loop to `Task.Run` inside GUI code does not satisfy this boundary. The computation still belongs to the layer that owns its meaning.

## Responsiveness contract

The Avalonia UI thread must remain responsive while Berries performs work whose runtime can grow materially with Corpus or Portrait size.

Any operation that may take appreciable time must therefore:

1. execute outside the GUI thread through an asynchronous Core or Projection API;
2. accept a `CancellationToken`;
3. check cancellation within the loop whose work scales with the data size, with sufficiently fine granularity that cancellation remains responsive;
4. report a meaningful user-facing phase description;
5. report `Completed` and `Total` whenever the total work can be determined cheaply enough that counting it does not itself become disproportionate work.

A merely asynchronous wrapper is insufficient if an expensive inner traversal or sort remains effectively uncancellable.

### Determinate progress is preferred

When the work population is already known or cheaply countable, progress should be determinate. Typical examples include:

    files in an acquired Portrait
    candidate files to hash
    Groups to inspect
    files within known Groups
    known Directory/Branch records
    known candidate relationships

The status bar should show both the phase and count, for example:

    Constructing Groups — 12,400 / 18,250

### Indeterminate progress is acceptable when necessary

Some operations do not know their total cheaply in advance. Filesystem enumeration is the principal example: determining the total may require essentially performing the enumeration twice.

In those cases Berries should still report a meaningful phase and useful observed counts where available, while the progress bar remains indeterminate.

Indeterminate progress should be a consequence of genuinely unknowable or disproportionately expensive totals, not convenience in the implementation.

## Progress ownership

Core and Projection report progress as data. They do not manipulate GUI controls.

The GUI owns only presentation of that progress in the status bar. A progress report should contain enough information for the GUI to choose determinate or indeterminate display without understanding or reimplementing the computation.

The layer performing the work owns the wording of the computational phase because it knows what work is actually occurring. The GUI may add surrounding interaction context, but it should not hard-code a misleading description of a multi-phase Core operation.

## Cancellation and correctness

Cancellation is required for responsiveness and efficiency, but cancellation is not the correctness mechanism for generation-dependent background analysis.

Derived analysis is computed against a stable Portrait generation. When the Working Portrait changes:

- the previous generation becomes stale immediately;
- obsolete work is asked to cancel;
- stale results are prevented from publishing by generation validation;
- current-generation analysis is scheduled.

Thus prompt cancellation avoids wasted computation, while generation validation prevents stale results from becoming authoritative.

## Application orchestration

`BerriesApplication` owns application-level sequencing that crosses Core computations: session replacement, serialized portrait mutation, portrait generation, and dependency-driven analysis scheduling.

The GUI does not maintain a second analysis scheduler, cancellation lifecycle, or refresh queue. It observes Core progress and product publication and updates capabilities accordingly.

## Projection boundary

Projection is not a general-purpose place to move expensive work out of the GUI. Computation belongs in Projection only when its result is itself presentation-shaped.

For example:

- determining which Groups exist is Core;
- determining which Branches share content is Core;
- building a `GroupProjection` label and presentation ordering is Projection;
- constructing a Branch Explorer hierarchy from already-established factual placements is Projection;
- creating Avalonia `TreeView` controls or assigning `ItemsSource` is Gui.

This distinction keeps the semantic computation reusable and testable independently of any current Explorer presentation.

## Review rule

When adding or changing a loop whose runtime scales with user data, review it explicitly for:

    correct architectural layer
    asynchronous call boundary
    cancellation granularity
    meaningful phase reporting
    determinate progress where practical
    absence of equivalent work on the GUI thread

A responsiveness regression is an architectural regression even if the result is functionally correct.
