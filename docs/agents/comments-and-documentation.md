# Comments and documentation

Applies to code comments, AGENTS.md, README and every other document in this repository.
- User-facing writing in `spec/` and `README.md` follows https://ant.design/docs/spec/copywriting.
- Name packages and repositories exactly: `Enclave.Sdk.Api`, `Enclave.Sdk.Api.Data`, the sdk repository. NEVER call `Enclave.Sdk.Api` "the SDK".
- Plain, factual register; no intensifiers ("critically", "truly", "real").
- State things as they are. NEVER frame a statement as a contrast ("X, not Y", "rather than", "instead of") unless the alternative is the point of the sentence.
- Behaviour that depends on an external system (OS, API, library) cites its primary source: URL, spec section, or file and member, with the version it applies to. NEVER cite a line number: lines move with every change to the cited file, and nothing flags a citation that has drifted.
- Exception: `Enclave.Sdk.Api` and `Enclave.Sdk.Api.Data` are cited without a version, and documents never record which release added what. Their citations apply to the versions `Directory.Packages.props` pins, with `Enclave.Sdk.Api.Data` taken through `Enclave.Sdk.Api`. The cost is that a version bump re-checks every citation of them against the diff between releases; that is less work than rewriting a version number in every comment on each bump.
- No status or timeline wording: "today", "currently", "until fixed".

Code comments also:
- Comments say WHY: the reason for the code, a constraint, a trade-off, or the external behaviour it relies on. Say WHAT only where the code is complex enough that a reader cannot see it quickly. Delete a comment that restates the code, the member name or the spec.
- Keep a comment to its reason, in a sentence or two; leave out the evidence that led to it. Where the reason is a rule in `spec/cli-surface.md`, name the section ("Several IDs") and leave the rule there.
- Give a reason once, where the behaviour lives. Code that repeats the behaviour shares it (a helper), and does not repeat the comment.
- A trade-off states what was given up and why that is acceptable.
- Correct a wrong comment in place.
- Comments are `//` prose. NEVER write XML doc comments (`///`). What a caller needs that the signature cannot show (for example, null when the file is missing) opens the comment above the member, before the reason. Trade-off: the compiler does not check names in a comment, and IDE hover text does not show it; accepted because nothing outside this repository uses these assemblies.
