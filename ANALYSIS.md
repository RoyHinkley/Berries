# Berries Analysis Design

This document defines how Berries constructs the objective analysis state from a Corpus and how Cases are discovered and presented for attention. Terminology and semantic invariants are defined in `MODEL.md`.

## Generate the Initial Portrait

Allow selection of filesystem directories. Selected directories become Corpus roots.

Input roots are normalized: canonicalize paths, remove exact duplicates, and discard any selected root contained by another selected root. The retained Corpus therefore contains the minimal disjoint root set.

Scan every regular File beneath the roots and record path and required platform-neutral metadata. Filesystem-specific metadata, when useful, is obtained only through the filesystem abstraction.

At this stage Content identity is generally unknown.

Unsupported symbolic/special filesystem objects are handled by the platform adapter and are not exposed to Core as ordinary Files.

## Enumerate DuplicateSets

Enumerate all Files in the Portrait and group by length.

Files in singleton-size groups require no hashing for duplicate detection and remain represented as unique Files.

For each non-singleton size group, hash candidate contents sufficiently to establish reliable Content identity. Progressive/prefix hashing is an optional optimization; the present implementation may proceed directly to full hashing when that is simpler and measured performance is acceptable.

Partition equal hashes into exact DuplicateSets. Singleton hash groups are discarded from duplicate analysis but their Files remain in the Portrait.

Each DuplicateSet represents one distinct duplicated Content identity.

If a File becomes unavailable or inaccessible during an operation, it is evicted from the Current Portrait for the remainder of the session. Programming failures are not converted into file evictions.

## Subtract settled duplicate relationships

DuplicateSets are physical facts about the Portrait and are not rewritten when the user accepts duplication.

A separate `DuplicateSettlements` state records duplicate relationships that no longer require a user decision. It supports both:

    whole-Content acceptance
        every relationship represented by one DuplicateSet is settled

    pairwise acceptance
        one specific pair of equal-Content File instances is settled while
        other relationships involving the same Content can remain unresolved

Downstream structural analysis operates on unresolved duplicate relationships only. Thus acceptance can reduce the Case population even when the filesystem Portrait itself does not change.

This subtraction occurs when physical duplicate equality is converted into structural evidence. Hashing and DuplicateSet construction remain unaffected.

Whole-DuplicateSet settlement is particularly important because one widely repeated Content can induce many DirectoryPairs and many higher-level ScopePairs. One user decision can therefore remove substantially more future decision work than the DuplicateSet Case alone suggests.

## Build Directory records

Include directories represented by unresolved duplicate relationships.

Directory statistics describe directly contained Files only. Descendants are not folded into local counts.

Maintain at least:

    Path
    FileCount
    DuplicateFileCount
    DuplicateContentCount

`FileCount` remains a physical direct-file count for the directory. `DuplicateFileCount` and `DuplicateContentCount` describe unresolved duplicate evidence only.

A directory is a candidate Single-directory Case whenever at least one Content has more than one unresolved File-instance relationship directly in that directory. External instances of the same Content do not suppress the Case.

## Build the sparse DirectoryPair graph

DirectoryPair describes direct/local unresolved shared Content between two directories.

For each unresolved DuplicateSet relationship:

    determine the directly represented directories
    for every unordered directory pair having at least one unresolved instance pair:
        contribute that Content once to DirectoryPair(A,B)

Thus DirectoryPair leverage is the number of distinct unresolved duplicated Contents occurring directly in both directories.

The DirectoryPair population is also a useful weighted undirected graph:

    directory = vertex
    DirectoryPair = edge
    DirectoryPair leverage = edge weight

Cheap graph characteristics are retained or derived because empirical testing shows that they distinguish useful structural phenomena:

    degree
        number of other directories sharing unresolved duplicated Content

    weighted degree
        sum of incident DirectoryPair leverage

    maximum incident edge leverage

    mean incident edge leverage

    strongest-edge concentration
        maximum edge leverage / weighted degree

    connected components
    largest component size
    graph density among participating directories

For a DirectoryPair, derive:

    directional coverage
        shared Content / duplicated Content represented by each endpoint

    Jaccard overlap

    edge concentration at each endpoint
        edge leverage / endpoint weighted degree

These are objective evidence, not semantic classification.

Empirically, high coverage plus high concentration identifies a very different relationship from high coverage embedded in a large diffuse hub. For example, standardized generated/template directories can form exact-content cliques with very low edge concentration.

## Build ScopePairs

A Scope rooted at A contains A and all descendants.

Scope analysis consumes the already-constructed DirectoryPair graph. It does not independently reconstruct directory-pair evidence from DuplicateSets.

Each DirectoryPair contributes its weight to containing pairs of ancestor-or-self scopes.

A ScopePair has two effective sides. For non-overlapping roots these are the ordinary recursive scopes. For nested roots, the descendant subtree is omitted from the ancestor side and constitutes the other side. The two effective sides are always disjoint.

Only DirectoryPair evidence crossing both effective sides contributes ScopePair leverage.

Current experimental ScopePair leverage is the **weighted cut size**: sum the DirectoryPair leverage crossing the effective-side cut. Equivalently, a duplicated Content contributes once for each contributing DirectoryPair through which it crosses the cut. The same Content may therefore contribute more than once to a ScopePair.

This is intentionally treated as a structural payoff measure rather than an approximation that must eventually be corrected to an exact distinct-Content count. Real-corpus testing showed that exact and weighted ScopePair leverage can produce materially different Case orders; which ordering is more useful remains an empirical question because the actual objective is reduction of user decision work, not preservation of a particular leverage definition.

DirectoryPairCount records the number of distinct direct DirectoryPairs contributing evidence to the ScopePair.

A ScopePair can exist even when its root directories share no direct Files.

Example:

    A\Family <-> B\Family
    A\Trips  <-> B\Travel

can induce:

    ScopePair(A,B)

without DirectoryPair(A,B).

### Nested ScopePairs as cuts

A nested ScopePair is a partition of a containing tree, not merely a large bounding box around a smaller one.

For:

    A <-> B

where B is inside A, the effective sides are:

    A excluding B <-> B

If the descendant root moves downward from B to B\C, the effective sides become:

    A excluding B\C <-> B\C

The part of B outside B\C moves from the descendant side to the ancestor side. Duplicate relationships formerly internal to B can therefore become cross-cut relationships, while other relationships can cease crossing the cut.

Consequently, leverage can increase or decrease as a ScopePair boundary moves downward. This reflects real repartitioning of the same objective duplication graph.

This makes boundary movement itself useful structural evidence. Related ScopePairs reached by moving roots through the directory hierarchy should not automatically be treated as strict subset/refinement relationships.

## Construct candidate Cases

Cases are objective, program-discovered bounded sets of Files derived from the Current Portrait plus the current unresolved-duplication state. Situations are not needed for discovery.

Candidate Case types and bounds:

    DuplicateSet Case
        all File instances in one DuplicateSet having at least one unresolved
        duplicate relationship

    Single-directory Case
        all Files directly contained by a directory having unresolved internal
        duplication

    DirectoryPair Case
        all Files directly contained by either directory in the unresolved pair

    ScopePair Case
        all Files on the two effective disjoint ScopePair sides

Structural Cases include unique Files as defined by their bounds because those Files may matter to a Disposition.

The defining unresolved duplication pattern supplies the reason for considering the Case. A Case need not resolve unrelated duplication within its bounds; see `MODEL.md`.

Cases may overlap. A Resolution can therefore alter the unresolved evidence that causes other Cases to exist even when its Disposition makes no filesystem change.

To avoid unnecessary memory expansion, lightweight Case candidates can be ranked before bounded File sets are materialized. Materialize only Cases actually needed for presentation or further analysis.

## Leverage and presentation priority

Leverage remains one objective structural measurement. It is not the program objective and is not presumed to define the best Case order.

For DuplicateSet, Single-directory, and DirectoryPair Cases it remains an exact distinct-unresolved-Content count. For ScopePairs the current experiment uses weighted crossing evidence as described above.

The actual objective is to reduce the user's remaining decision work. A useful Resolution can accomplish that by removing duplicate Files, by restructuring them, or simply by establishing that some duplication is acceptable. Therefore a Case can have high decision impact even when its Disposition leaves the Portrait unchanged.

Early implementation ranked Cases only by descending leverage. Real-corpus testing showed that this is insufficient. Exact distinct-Content ScopePair leverage and weighted structural leverage produce materially different orderings, and neither has yet been established as generally superior.

Broad ancestor ScopePairs can also have the highest leverage while substantially narrower or differently placed cuts present much clearer questions. Maximum information gain can therefore occur below the leverage maximum.

The governing presentation heuristic remains:

    Ask the smallest comprehensible question with the greatest downstream
    simplifying effect.

Useful objective dimensions already identified include:

    leverage / structural weight
        potential reach of the defining duplicate evidence

    breadth / specificity
        how much filesystem structure is brought into the question

    coverage
        how completely each side participates in the defining relationship

    concentration
        whether evidence is focused in a few strong correspondences or diffuse
        across many weak ones

    structural depth / boundary position
        whether the Case represents a broad or more localized cut/relationship

    directional containment
        whether nearly all duplicated Content on one side is represented on
        the other while the reverse is not true

    settlement impact
        how much unresolved Case-generating structure could disappear if one
        user answer settles the Case

Case ordering is therefore an open empirical problem. Do not prematurely collapse these characteristics into an unexplained weighted scalar.

## Prospective settlement experiments

A useful way to evaluate decision impact is to compare two analyses over identical physical data:

    baseline
        analyze the unresolved duplicate state normally

    prospective settlement
        accept one candidate DuplicateSet or relationship without changing the
        Portrait, then rebuild downstream analysis

Compare at least:

    unresolved DuplicateSet Cases
    Single-directory Cases
    DirectoryPairs
    ScopePairs
    total Cases
    graph components and largest component
    top-Case membership/order
    phase timing

The current exploratory GUI automatically selects a broad same-name, one-instance-per-directory DuplicateSet candidate, performs such a non-mutating A/B rerun, and reports the resulting deltas. This is diagnostic machinery, not a presentation heuristic or automatic settlement policy.

One promising future phenotype is a same-name Content appearing once in many otherwise weakly related directories. `.gitignore`-type files and standard repository hook samples are examples, but production heuristics should be expressed in objective structural characteristics rather than hard-coded filenames.

User-approved rules may eventually generalize a Resolution across objectively identifiable similar Cases. A heuristic can suggest similarity; a rule would explicitly authorize applying the same Resolution to matching Cases.

## Exploratory structural diagnostics

For sampled ScopePairs, derive on demand rather than materializing a second enormous graph:

    whether roots are nested
    duplicated Content count on each effective side
    cross-side coverage
    contributing DirectoryPair count
    strongest contributing DirectoryPairs
    related/subordinate ScopePairs from hierarchy movement
    leverage ratio relative to the reference ScopePair
    DirectoryPair evidence ratio relative to the reference ScopePair

Contributing DirectoryPairs are often more explanatory than duplicated-file samples because they show where the cross-cut relationship actually occurs.

The related ScopePair population is also informative, but ancestry alone must not be interpreted as strict set refinement for nested cuts.

## Empirical development method

Do not implement Situation inference before understanding the objective case space.

Use real corpora to inspect small, high-value samples of Cases. Characterize objective structural patterns first, then ask which Situations are compatible with those patterns.

As characteristic regions become understood, they can be marked empirically covered so subsequent samples expose new regions of the case space. "Covered" does not mean the Situation is known; it only means the objective structural phenotype has already been examined.

If objective evidence does not narrow the applicable Situations, that is a valid result. The user supplies the Situation.

## Refresh derived analysis after Apply or settlement

Cases and structural indexes are derived data.

After a virtual Apply or a new settlement, invalidate/recompute affected portions of:

    unresolved DuplicateSet evidence
    Directory records
    DirectoryPair relationships and graph metrics
    ScopePair relationships
    candidate Cases
    presentation ordering

Do not reread the filesystem or rehash known Content merely because the virtual Portrait or settlement state changed. Delete, move/rename, and copy operations preserve known Content identity; acceptance changes decision state, not physical Content identity.

Correct incremental invalidation is desirable, but broader recomputation from the in-memory Portrait and known DuplicateSets is acceptable initially if performance permits.
