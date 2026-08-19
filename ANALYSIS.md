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

## Build Directory records

Include directories represented by Files in DuplicateSets.

Directory statistics describe directly contained Files only. Descendants are not folded into local counts.

Maintain at least:

    Path
    FileCount
    DuplicateFileCount
    DuplicateContentCount

Each distinct duplicated Content contributes once to DuplicateContentCount regardless of how many instances occur directly in the same directory.

A directory is a candidate Single-directory Case whenever at least one Content has more than one File instance directly in that directory. External instances of the same Content do not suppress the Case.

## Build the sparse DirectoryPair graph

DirectoryPair describes direct/local shared Content between two directories.

For each DuplicateSet:

    determine the distinct parent directories represented by its instances
    for every unordered pair of distinct represented directories:
        DirectoryPair(A,B).Leverage += 1

Thus DirectoryPair leverage is the number of distinct duplicated Contents occurring directly in both directories.

The DirectoryPair population is also a useful weighted undirected graph:

    directory = vertex
    DirectoryPair = edge
    DirectoryPair leverage = edge weight

Cheap graph characteristics are retained or derived because empirical testing shows that they distinguish useful structural phenomena:

    degree
        number of other directories sharing duplicated Content

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

Each DirectoryPair contributes evidence to containing pairs of ancestor-or-self scopes.

A ScopePair has two effective sides. For non-overlapping roots these are the ordinary recursive scopes. For nested roots, the descendant subtree is omitted from the ancestor side and constitutes the other side. The two effective sides are always disjoint.

Only duplicated Content represented across both effective sides contributes ScopePair leverage.

Current ScopePair leverage is **exact**: count distinct duplicated Contents crossing the effective-side cut. Do not sum descendant DirectoryPair weights in a way that can count the same Content more than once.

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

Consequently, leverage can increase or decrease as a ScopePair boundary moves downward. This is not approximation error; it is real repartitioning of the same objective duplication graph.

This makes boundary movement itself useful structural evidence. Related ScopePairs reached by moving roots through the directory hierarchy should not automatically be treated as strict subset/refinement relationships.

## Construct candidate Cases

Cases are objective, program-discovered bounded sets of Files in the Current Portrait. Situations are not needed for discovery.

Candidate Case types and bounds:

    DuplicateSet Case
        all File instances in one DuplicateSet

    Single-directory Case
        all Files directly contained by a directory having internal duplication

    DirectoryPair Case
        all Files directly contained by either directory in the pair

    ScopePair Case
        all Files on the two effective disjoint ScopePair sides

Structural Cases include unique Files within their bounds because those Files may matter to a Disposition.

The defining duplication pattern supplies the reason for considering the Case. A Case need not resolve unrelated duplication within its bounds; see `MODEL.md`.

Cases may overlap. Applying one Case can therefore alter or invalidate others.

To avoid unnecessary memory expansion, lightweight Case candidates can be ranked before bounded File sets are materialized. Materialize only Cases actually needed for presentation or further analysis.

## Leverage

Leverage is defined in `MODEL.md` and is retained as an objective payoff measure.

It answers:

    How many distinct duplicated Content relationships does this Case's
    defining structure directly address?

It does not answer:

    How many duplicate Files happen to be somewhere inside the Case bounds?

Bytes are not part of leverage. The objective is to reduce unwanted duplication and human decision workload, not primarily reclaim storage.

## Structural evidence and Case presentation priority

Early implementation ranked Cases only by descending leverage. Real-corpus testing showed that this is useful but insufficient.

Broad ancestor ScopePairs can have the highest leverage while substantially narrower or differently placed cuts retain nearly as much leverage and present much clearer questions. Maximum information gain can therefore occur below the leverage maximum.

The governing presentation heuristic remains:

    Ask the smallest comprehensible question with the greatest downstream
    simplifying effect.

Leverage measures the second half. Objective structural characteristics help estimate the first.

Useful dimensions already identified include:

    leverage
        potential duplicate-decision reduction

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

Do not prematurely collapse these into an unexplained weighted scalar. Multi-objective/Pareto-style comparison and structural dominance are preferred experiments.

A related ScopePair with lower leverage may still be preferable when it presents a substantially clearer, narrower question. Conversely, a related ScopePair can legitimately have higher leverage because moving a nested cut can expose additional cross-boundary duplication.

Therefore subsidiary/related ScopePair leverage ratios are evidence, not monotonic "retention" guarantees.

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

## Refresh derived analysis after Apply

Cases and structural indexes are derived data.

After a virtual Apply, invalidate/recompute affected portions of:

    DuplicateSet membership
    Directory records
    DirectoryPair relationships and graph metrics
    ScopePair relationships
    candidate Cases
    presentation ordering

Do not reread the filesystem or rehash known Content merely because the virtual Portrait changed. Delete, move/rename, and copy operations preserve known Content identity.

Correct incremental invalidation is desirable, but broader recomputation from the in-memory Portrait is acceptable initially if performance permits.
