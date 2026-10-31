# Default personal knowledge libraries to private with explicit school sharing

Users own a personal knowledge library whose contents are private by default; teachers may explicitly share selected content with a specified school. Library ownership, private indexing, and school sharing are separate concepts, so making material searchable for its owner does not publish it to other users.

This decision supersedes ADR 0002's default-all visibility for the new personal-library flow only. ADR 0005's historical empty-scope compatibility must not make new personal content public: a personal library always has an explicit owner boundary, and library access is enforced together with item-level policy for lists, attachments, retrieval, generation, and derived content. Existing organizational content retains its recorded access semantics until an explicit migration is designed.

School membership alone does not grant access to unshared personal material. The exact mapping between schools, accounts, and existing tenants, including transfers between schools, remains to be designed; this decision does not assign one tenant per teacher or silently reclassify existing knowledge.
