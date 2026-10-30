# AGENTS.md

Working with this repo:

- `spec/cli-surface.md`: commands, flags, defaults, examples. "Who it is for" governs all input and output.
- `docs/agents/project.md`: layout, build, test.
- `docs/agents/architecture.md`: API access, scope.
- `docs/agents/code.md`: code rules.
- `docs/agents/testing.md`: before writing a test.
- `docs/agents/cli-contract.md`: before changing command input or output.
- `docs/agents/comments-and-documentation.md`: before writing a comment or doc.
- `docs/agents/ci.md`: before attempting to make changes local the git repo state.

Change workflow:

1. Update `spec/cli-surface.md`.
2. Write a test for the behaviour.
3. Run it; it MUST fail because the new behaviour is missing.
4. Write the minimum code to pass it.
5. Run the full suite; the build MUST be warning-free.

Rules:

MUST and NEVER are binding.

- NEVER skip, weaken or delete a failing test to get green; report it.
- `[Category(TestCategory.Pending)]` marks a test for behaviour the CLI doesn't meet; CI skips it. Remove it when the test passes. NEVER add it to a passing or newly broken test.
- NEVER commit, push, tag, or open pull requests or releases.
