# Berries Problem Boundary — Empirical Working Notes

This document records what real-corpus experiments have established about the practical problem boundary of Berries. It is deliberately a working research document, not a design commitment. Its purpose is to prevent each newly observed kind of duplication from automatically expanding the Situation catalogue or the intended authority of the program.

## Why a boundary is needed

Berries is already effective at discovering objective duplicate structure. Exact DuplicateSets, DirectoryPairs, and BranchPairs expose genuine relationships in real corpora.

The difficult question is not whether two File instances have identical Content. It is whether that duplication represents a problem Berries can sensibly help the user resolve.

Real corpora contain substantial intentional or application-required duplication. Some identical File instances are independently required because their paths or containing structures have semantics imposed by software outside Berries. Therefore:

    duplicate Content does not imply removable redundancy

and:

    analytical relevance does not imply disposition authority

A File may provide valuable evidence about relationships among directories or branches even when Berries has insufficient grounds to propose deleting, moving, or otherwise modifying it.

## Findings established so far

### Objective duplicate structure is readily discoverable

DuplicateSets provide exact Content identity. DirectoryPairs and BranchPairs expose increasingly broad structural relationships derived from unresolved duplicate evidence. Real-corpus testing has repeatedly found recognizable relationships rather than arbitrary noise.

The remaining difficulty is semantic: determining which objective relationships correspond to coherent questions the user can answer and which merely reflect implementation details of external systems.

### Semantic normalization before structural analysis is valuable

A widely distributed identical File can create many DirectoryPairs and, through them, many BranchPairs even when there is no meaningful relationship among the containing directories beyond that one distributed Content.

The early distributed-DuplicateSet screening experiment demonstrated that some of these Cases are readily understandable at the DuplicateSet level. In the AeonHacs corpus, the user accepted 48 retain-all DuplicateSets. Relative to the immediately preceding no-settlement baseline:

    DirectoryPairs    1,734 -> 1,153    reduction 581 (33.5%)
    BranchPairs      11,141 -> 7,971    reduction 3,170 (28.5%)
    total Cases      13,623 -> 9,824    reduction 3,799 (27.9%)

This is not merely a performance optimization. The removed higher-level Cases were partly consequences of promoting individually explainable duplication into weaker directory- and branch-level relational contexts.

The emerging principle is:

    Resolve duplication at the lowest structural level at which the evidence
    supports a coherent question encompassing it.

This is intentionally not "always resolve the lowest-level Case first." Several DuplicateSets may share enough structural context that a DirectoryPair or other containing Case is the more coherent question.

### Some distributed DuplicateSets are easy for a user to recognize

The current exploratory screen identifies DuplicateSets where:

    every instance has the same filename
    exactly one instance occurs in each represented directory
    at least three directories are represented

Many candidates were immediately recognizable as intentional distributed copies. Their natural Resolution was retain all duplicates.

The filename itself proved useful but insufficient context. Hover detail showing full instance paths was added because location can make an otherwise opaque filename understandable without cluttering the primary presentation.

A likely future experiment is whether exact filename matches dominate user-approved retain-all settlements strongly enough to support persistent filename-based rules or preselection. This has not yet been measured.

### Multiple low-level Cases can form one coherent repeated structure

The repeated `.sample` Files under Git hook directories exposed an important counterexample to mechanically preferring DuplicateSet Cases. Individually, each Content resembles a sprinkled identical File. Collectively, however, many DuplicateSets appear together in nearly the same set of corresponding directories.

This suggests an objective structural detector based on similar parent-directory incidence. A repeated bundle of Contents may indicate that the containing directories form the lowest level at which one coherent question encompasses the evidence.

This possibility should be investigated generically. Berries should not need special knowledge of Git hooks to observe that many Contents repeatedly co-occur in corresponding directory locations.

### Application-managed structures can contain useful but dangerous duplicate evidence

Git object storage provided the clearest observed example. Opaque hexadecimal-looking filenames occur beneath `.git\objects` in multiple related repositories. Identical objects in multiple repositories are genuine duplicate Content and can be highly informative evidence that the repositories share history.

They are not thereby sensible deletion candidates. Their pathname and presence are managed by Git; deleting an instance because an identical object exists in another repository can damage the repository.

This establishes an important distinction:

    analytical participation
        May this File contribute evidence about duplicate relationships?

    destructive authority
        Does Berries have sufficient grounds to propose modifying this File?

These properties need not be equal.

## Tentative domain partition

The following partition is a hypothesis to guide further testing, not a final classification system.

### User-managed material

Likely central to the intended problem domain.

Examples include personal documents, photographs, downloads, manually organized collections, old machine copies, backups, archives, migrations, staging areas, and reorganized directory trees.

Here pathname and directory organization are generally part of the user's own organization of the material. Existing Situations such as Unneeded copy, Move residue, Reorganization, Backup, Migration, Snapshot, Archive, Staging / Import, Downloads, and Mirror plausibly describe recurring causes of duplication.

Berries should analyze this material and may propose ordinary Resolutions and Dispositions when justified by the selected Situation.

### Externally/application-managed material

Potentially useful as analytical evidence but unsafe as an ordinary file-level deduplication target.

Observed example:

    Git internal storage

Plausible but not yet empirically established examples include package stores, caches, databases, generated application state, build intermediates, and other structures whose paths have application-defined semantics.

The important abstraction may be "application-managed / structurally opaque" rather than a catalogue of producing applications.

### Mixed trees

Source trees demonstrate that corpus selection alone may not establish the boundary. A tree can contain user-managed source Files, generated output, and application-managed `.git` state simultaneously.

A future mechanism may therefore allow some regions to participate in analysis while withholding or restricting destructive Dispositions within them.

## Situation catalogue implications

The discovery of Git objects, generated binaries, hook samples, caches, or other application artifacts should not automatically create new Situations.

A Situation should describe a useful semantic cause of duplication that evokes one or more natural Resolutions. Application identity is often orthogonal to that purpose.

If many producer-specific artifacts can instead be handled through a small number of management/safety properties, the Situation catalogue can remain pragmatic and compact.

Conversely, if ordinary user-managed corpora continually require numerous nuanced producer-specific Situations, that would be evidence that the present semantic approach does not scale.

## Possible `.berriesignore` boundary mechanism

A `.berriesignore` mechanism is a plausible practical way to constrain analysis or disposition authority, particularly for recognizable application-managed subtrees. Its exact semantics are deliberately unresolved.

Possible meanings include:

    exclude matching Files/subtrees from the Portrait entirely

    retain matching Files as analytical evidence but exclude them from
    destructive Dispositions

    suppress particular Case types while retaining higher-level evidence

These alternatives are materially different. The Git-object experiment argues against assuming that complete analytical exclusion is always best, because application-managed duplication can provide useful structural evidence.

A future ignore/protection mechanism should therefore be designed only after testing establishes whether such evidence materially improves useful Case discovery.

## Open questions

The present data does not establish answers to the following:

1. Can application-managed regions be identified by a small number of general objective characteristics, or would this degenerate into an application-specific exception catalogue?

2. Is explicit user configuration such as `.berriesignore` sufficient for the difficult regions?

3. Should ignored/protected material disappear from analysis entirely, or remain available as evidence while being excluded from destructive authority?

4. Can repeated Content-incidence patterns identify coherent managed structures without knowing which application produced them?

5. How much of Berries' useful problem domain remains after application-managed structures are excluded from ordinary disposition authority?

6. Do ordinary personal-data corpora converge quickly onto the existing Situation catalogue, or do they exhibit the same continuing proliferation of special cases seen in developer source trees?

7. Where should generated outputs fall? They may be user-disposable in some contexts and application-required in others.

8. Does semantic normalization at the DirectoryPair level produce downstream reductions comparable to the demonstrated DuplicateSet screening effect?

9. Can multiple objectively similar Cases be resolved by one user-approved rule without requiring Berries to infer semantic truth?

10. Do exact filename matches dominate retain-all distributed DuplicateSets strongly enough to support simple persistent rules?

## Next empirical phase

Developer source trees have been valuable because they exposed difficult boundary conditions early, but they are strongly enriched in Git state, build outputs, binaries, package artifacts, and other managed structures. They should not remain the principal evidence for defining Berries' practical domain.

The next tests should use real corpora containing no source code and preferably representing qualitatively different kinds of accumulated user data. Useful examples include:

    ordinary personal documents/photos/downloads
    an old backup or migrated-computer tree
    a deliberately messy mixed personal collection

For each corpus, record not only Case counts and structural metrics but also the user's qualitative experience:

    Which early DuplicateSet questions were immediately understandable?
    Which retain-all decisions shared exact filenames?
    Which Cases appeared to belong to a common repeated structure?
    Which Cases were opaque or unsafe to answer?
    Which Situations were actually needed?
    Did new Situations represent genuinely recurring user concepts or merely
    producer-specific special cases?

The purpose of this phase is not to prove that Berries can understand arbitrary filesystems. It is to determine the smallest defensible domain in which Berries consistently reduces the user's duplicate-resolution work without requiring an unbounded catalogue of special cases.

## Current working hypothesis

A practical Berries may ultimately concentrate on user-managed material while treating application-managed structures as protected or opaque regions that can, when useful, still contribute analytical evidence.

There is not yet enough diverse real-world data to adopt that boundary as a design commitment. The next non-source-code corpora should be treated explicitly as tests of this hypothesis rather than as opportunities to add new special cases whenever unfamiliar duplication appears.
