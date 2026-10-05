# AGENTS.md

Directives for AI agents working in this repository. MUST and NEVER are binding.

## Repository
- Product: `enclave-cli`, a command-line front door to the Enclave Management APIs (https://api.enclave.io).
- Primary users: AI agents. Secondary: human operators. Design every output and error for machine parsing first.
- Public repository, MIT licence.
- `src/Enclave.Cli/`: the CLI. Assembly `enclave-cli`, namespace `Enclave.Cli`.
- `tests/Enclave.Cli.Tests/`: NUnit tests.
- `.github/workflows/ci.yml`: build, test, package, release.
- Commands that control the local agent or machine belong to `enclave`, the Enclave agent (repository enclave-networks/fabric). NEVER add them here.

## Commands
- Build: `dotnet build enclave-cli.slnx -c Release`
- Test: `dotnet test enclave-cli.slnx -c Release`
- One test: `dotnet test enclave-cli.slnx -c Release --filter "FullyQualifiedName~<TestName>"`
- Publish one platform (native AOT): `dotnet publish src/Enclave.Cli/Enclave.Cli.csproj -c Release -r <rid> -o publish`
  - Only for the OS you are on. Windows needs the Visual Studio C++ tools, with `vswhere.exe` on PATH; Linux needs `clang` and `zlib1g-dev`; macOS needs Xcode.
  - A Linux binary built locally needs the build machine's glibc or newer. Release Linux binaries come from CI's containers.
- RIDs shipped: `win-x64 win-arm64 linux-x64 linux-arm64 linux-musl-x64 linux-musl-arm64 osx-x64 osx-arm64`
- Local builds are versioned `0.0.0-dev`; only CI sets a release version.

## Workflow: test-driven, mandatory
1. Write the test for the required behaviour first.
2. Run it. Confirm it fails, and fails for the expected reason. A test that passes before the code exists guards nothing: rewrite it.
3. Write the minimum code that makes it pass.
4. Run the full suite. Warnings are errors; the build MUST be clean.
5. NEVER skip, weaken or delete a failing test to get green. Report it.

## Testing
- NUnit 4: `Assert.That`, `Assert.Multiple`.
- Run the CLI in-process and assert exit code, stdout and stderr:
  `await Program.CreateRootCommand().Parse(args).InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr })`
- Fake the Enclave API over HTTP with WireMock.Net on loopback; point the SDK at it with `EnclaveClientOptions.BaseUrl`. enclave.sdk.api's own tests use the same pattern.
- Assert the request the fake received (method, path, query, body) as well as the CLI's output. A test MUST fail if the CLI sends the wrong request, or none.
- NEVER call the live API. NEVER read the real `~/.enclave/` or user profile in tests; inject paths and environment.
- CI runs the tests on Windows, Linux and macOS, on x64 and arm64. NEVER depend on OS-specific behaviour (path separators, line endings, case sensitivity) without handling it.
- Test names: sentences joined with underscores that state the required behaviour, e.g. `Systems_list_prints_json_by_default`.
- Test names and comments state required behaviour. NEVER describe the code's current state: no "fails today", "the bug", "until fixed", "still", "currently".

## Architecture
- Every API call goes through the `Enclave.Sdk.Api` package. NEVER use `HttpClient` or build requests in this repository.
- An endpoint or field the SDK lacks: stop and report it. It is added to enclave-networks/enclave.sdk.api first, with tests there, then the `Enclave.Sdk.Api` version is bumped here. NEVER work around a gap locally.
- One command makes one SDK call; when given several IDs, it makes the SDK's bulk call.
- Command shape: `enclave-cli <resource> <action> [args] [options]`. Resources map one-to-one to SDK clients: `org`, `systems`, `pending` (unapproved systems), `keys` (enrolment keys), `policies`, `tags`, `dns zones`, `dns records`, `trust` (trust requirements), `logs`.
- Out of scope: enrolling a system (the agent does that), account password/2FA/accepting invites (browser flows), payment.

## CLI contract (agent-facing; any change to it is a breaking change)
- stdout: JSON by default. Emit SDK models unchanged, so field names match the API. `-o table` is for humans; `-o id` prints one ID per line.
- Lists: `{ "items": [...], "total": <n>, "truncated": <bool> }`. `--limit <n>` and `--all`.
- stderr: one JSON object per error, `{ "error": { "code", "status", "title", "detail" } }`, carrying the API's problem details through. Nothing else goes to stderr unless `--verbose` is set.
- Exit codes: 0 success; 1 other API error; 2 bad arguments; 3 token missing or invalid; 4 token lacks the scope (HTTP 403); 5 not found (HTTP 404); 6 confirmation required.
- NEVER prompt, and NEVER wait on interactive input. delete, revoke, decline and remove without `--yes` exit 6 with an error naming `--yes`.
- Every command that changes something supports `--dry-run`: print the request it would send, send nothing, exit 0.
- `create` and `update` accept `--from-file <path|->` holding the SDK's create or patch model as JSON; `--template` prints a blank one.
- An ID argument of `-` reads newline-separated IDs from stdin.
- Times in output: ISO 8601, UTC. `--until` accepts ISO 8601 or a relative time (`2h`, `7d`).
- Token source, highest first: `--token`, `ENCLAVE_TOKEN`, `~/.enclave/credentials.json`. That file belongs to the SDK; NEVER change its format. CLI settings (default organisation) go in `~/.enclave/cli.json`.
- `enclave-cli commands --json` describes every command, option and type, and MUST stay complete.

## Code
- .NET 10, nullable enabled, file-scoped namespaces, `_camelCase` private fields. Rules: `.editorconfig` (matches enclave.sdk.api).
- Analyzers: `AnalysisMode=AllEnabledByDefault` plus StyleCop; `TreatWarningsAsErrors=true`. Fix the cause. Suppress only with a comment at the suppression giving the reason.
- Package versions live only in `Directory.Packages.props`. Restore uses nuget.org only (`NuGet.config`). NEVER add a private feed: forks and CI build without credentials.
- `InvariantGlobalization` is on. Format and parse with `CultureInfo.InvariantCulture`.
- Releases are native AOT (`PublishAot=true`). Code MUST be AOT- and trim-compatible: no reflection-based JSON (use a source-generated `JsonSerializerContext`), no `Reflection.Emit`, no unbounded `Type.GetType`/`Activator` use. AOT and trimming warnings are errors. NEVER suppress one.
- Build and test check only this repository's code for AOT problems. Problems inside packages (including `Enclave.Sdk.Api`) surface only at native publish, as `IL2104`/`IL3053` errors. Before handing back any change that adds or changes a package call, run the native publish for your OS and confirm it succeeds.
- `Enclave.Sdk.Api` 1.0.4 serializes JSON by reflection, which native publish rejects. Calling the SDK from the CLI needs an SDK release with source-generated serialization, made in the SDK repository (see Architecture).
- Tests run as JIT-compiled .NET, so they cannot catch a failure that exists only in the native binary. CI's smoke test (`.github/scripts/smoke-test.sh`) runs every published binary; extend it when a command depends on something AOT can break.

## Comments and documentation
Applies to code comments, AGENTS.md, README and every other document in this repository.
- Plain, factual register; no intensifiers ("critically", "truly", "real").
- State things as they are. NEVER frame a statement as a contrast ("X, not Y", "rather than", "instead of") unless the alternative is the point of the sentence.
- Behaviour that depends on an external system (OS, API, library) cites its primary source: URL, spec section, or file and function, with the version it applies to.
- No status or timeline wording: "today", "currently", "until fixed".

Code comments also:
- Give the reason for the code; the code itself shows what it does.
- A trade-off states what was given up and why that is acceptable.
- NEVER delete existing comments unless asked; correct a wrong one in place.
- XML docs: a short `<summary>`, plus `<param>`/`<returns>` where needed. No `<remarks>` essays.

## CI (`.github/workflows/ci.yml`)
- Pull request: version, then test + native publish + smoke test per RID on a runner of that OS and CPU, then upload archives (kept 14 days). No release.
- Linux RIDs build inside containers of the oldest supported distribution (`.github/docker`): AlmaLinux 8 (RHEL 8, glibc 2.28) for glibc, Alpine 3.22 for musl. The smoke test there proves the binary runs on it. Keep these minimums in step with README's supported platforms table.
- Push to main: the same, then the `release` job publishes GitHub release `v<version>` with eight archives and `SHA256SUMS`.
- Version: `YEAR.MONTH.DAY.RUN` in UTC, no leading zeros (e.g. `2026.10.5.57`), the scheme other Enclave products use. Pull requests add `-pr.<number>`. Computed once, in the `version` job.
- NEVER use `pull_request_target`. NEVER give a pull-request job a secret or write permission. Keep top-level `permissions: contents: read`; grant more per job.
- Pin every action to a full commit SHA with a `# vX.Y.Z` comment.
- Pass `${{ }}` values to `run:` scripts through `env:`.
- Lint workflow changes with actionlint.

## Git
- NEVER commit, push, tag, or open pull requests or releases. Leave changes in the working tree for the human to review.
