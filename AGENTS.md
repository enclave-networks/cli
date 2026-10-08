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
- `Enclave.Sdk.Api` is the Management API client package, built from repository enclave-networks/enclave.sdk.api. Repository enclave-networks/sdk is the agent's networking SDK and plays no part here.

## Commands
- Build: `dotnet build enclave-cli.slnx -c Release`
- Test: `dotnet test enclave-cli.slnx -c Release`
- Test as CI does (leaves out the Pending category): `dotnet test enclave-cli.slnx -c Release --filter "TestCategory!=Pending"`
- One test: `dotnet test enclave-cli.slnx -c Release --filter "FullyQualifiedName~<TestName>"`
- Publish one platform: `dotnet publish src/Enclave.Cli/Enclave.Cli.csproj -c Release -r <rid> -o publish` (a self-contained single-file executable). Release binaries come from CI.
- RIDs shipped: `win-x64 win-arm64 linux-x64 linux-arm64 linux-musl-x64 linux-musl-arm64 osx-x64 osx-arm64`
- Local builds are versioned `0.0.0-dev`; only CI sets a release version.

## Workflow: test-driven, mandatory
1. Write the test for the required behaviour first.
2. Run it. Confirm it fails, and fails for the expected reason. A test that passes before the code exists guards nothing: rewrite it.
3. Write the minimum code that makes it pass.
4. Run the full suite. Warnings are errors; the build MUST be clean.
5. NEVER skip, weaken or delete a failing test to get green. Report it.
6. Tests for behaviour the CLI does not meet carry `[Category(TestCategory.Pending)]` (`Support/TestCategory.cs`); CI leaves that category out. Remove the category in the change that makes the test pass. NEVER add it to a test that passes, or to a test that a change broke.

## Testing
- NUnit 4: `Assert.That`, `Assert.Multiple`.
- Run the CLI in-process through `CliRun` (`tests/Enclave.Cli.Tests/Support/`): `using var run = CliRun.Start(); var result = await run.RunAsync("system", "list");`. It gives the CLI its own environment, home directory, stdin, in-memory files (`run.Files`), a fake API and a fake partner API on separate addresses, and records exit code, stdout, stderr and every request.
- Tests never write to the disk: set up and check files through `run.Files`. A run that writes to the disk fails. Only `Storage/DiskFileStoreTests.cs` touches the disk, in its own temp directory, to prove `DiskFileStore` itself.
- Fake the Enclave API with WireMock.Net through `CliRun` or `LoopbackApi`, which listen on 127.0.0.1 only. A listener on every interface raises a Windows Firewall prompt and accepts connections from other machines. enclave.sdk.api's own tests use WireMock.Net the same way.
- Shared helpers live in `Support/`: `CliAssert` (outcomes), `JsonRead` and `JsonAssert` (JSON), `ApiJson` (API response bodies), `TestData` (paths, IDs and the test time zone), `FixedTimeProvider` (clocks). Add a helper there when a second test file needs it; NEVER copy one into a test class.
- Assert the request the fake received (method, path, query, body) as well as the CLI's output. A test MUST fail if the CLI sends the wrong request, or none.
- NEVER call the live API. NEVER read the real `~/.enclave/` or user profile in tests; inject paths and environment.
- CI runs the tests once, on Linux, in the `test` job; the build jobs run the smoke test only. NEVER write a test whose result depends on the OS. NEVER depend on OS-specific behaviour (path separators, line endings, case sensitivity) without handling it.
- Test names: sentences joined with underscores that state the required behaviour, e.g. `Systems_list_prints_json_by_default`.
- Test names and comments state required behaviour. NEVER describe the code's current state: no "fails today", "the bug", "until fixed", "still", "currently".

## Architecture
- Every API call goes through the `Enclave.Sdk.Api` package. NEVER use `HttpClient` or build requests in this repository.
- An endpoint or field `Enclave.Sdk.Api` lacks: stop and report it. It is added to enclave-networks/enclave.sdk.api first, with tests there, then the `Enclave.Sdk.Api` version is bumped here. NEVER work around a gap locally.
- The API and `Enclave.Sdk.Api.Data` (built from the portal repository) do not change. Use only routes and fields the API has; a model `Enclave.Sdk.Api.Data` lacks or has wrong is defined or corrected in `Enclave.Sdk.Api`.
- One command makes one `Enclave.Sdk.Api` call, with the exceptions listed in `proposed-cli-surface.md` "Calls per command": paging, name lookups, reads that keep tags, labels and conditions, and bulk calls of 200 IDs.
- Command shape: `enclave-cli <noun> <verb> [args] [options]`. A noun's own parts take hyphenated verbs (`dns create-hostname`, `org remove-user`). Nouns are singular, with a hidden plural alias: `org`, `partner`, `partner customer`, `system`, `key`, `policy`, `tag`, `dns`, `trust`, `log`.
- `proposed-cli-surface.md` is the reference for every command, flag, default and example. A command or flag it does not list is not added without changing it first.
- Out of scope: enrolling a system (the agent does that), account password/2FA/accepting invites (browser flows), payment, and the partner's own properties, users and invites (personal access tokens cannot carry those scopes).

## CLI contract (agent-facing; any change to it is a breaking change)
- stdout: JSON, always. Emit `Enclave.Sdk.Api` models unchanged, so field names match the API. There is no output-format option.
- Redirected stdout and stderr are UTF-8 without a byte order mark; stdin is read as UTF-8, or as a byte order mark at its start names, on every OS.
- Lists: `{ "kind": "<noun>", "items": [...], "total": <n> }`. A list reads every page and prints once all are read; a failed page read prints nothing. `log` is the exception: the newest 100 entries, `--limit <n>` or `--since <when>` for more.
- stderr: one JSON object per error, `{ "error": { "code", "status", "title", "detail", "errors" } }`, carrying the API's problem details through. `code` comes from a fixed set that `commands` lists. Nothing else goes to stderr unless `--verbose` is set.
- Exit codes: 0 success; 1 `api_error`, `not_implemented`; 2 `invalid_argument`, `no_org`, `no_partner`; 3 `token_missing`, `token_invalid`; 4 `forbidden` (HTTP 403); 5 `not_found` (HTTP 404, single-item commands); 6 `transient` (network failure, timeout, HTTP 429 or 5xx).
- NEVER prompt, and NEVER wait on interactive input. Changes run when given; there is no confirmation option.
- Every command that changes something supports `--dry-run`: print the request it would send, send nothing, exit 0.
- Every field is set with a flag; there is no file or JSON input. A create sends every setting that changes behaviour, with the value `proposed-cli-surface.md` gives when a flag is left out.
- An ID is never a positional argument. `--id` gives the command's own item; other items have `--<item>` for a name and `--<item>-id` for an ID. The option decides how a value is parsed; NEVER inspect a value to guess its type.
- `-` in place of the arguments reads a list printed by an `enclave-cli` list command; its `kind` must match the command. A command that takes several items takes any number, sends them in bulk calls of 200, and prints `{ "requested": n, "affected": m }`.
- Ranges, ACLs, gateway subnets and countries take an optional `=label`; an update keeps the labels of entries that stay.
- Times in output: ISO 8601, UTC. Input: a duration (`8h`), RFC 3339 with a zone, or a time without a zone read in the system's time zone. Formats never follow the system locale.
- Token source, highest first: `ENCLAVE_TOKEN`, `~/.enclave/credentials.json`. There is no token option. That file belongs to `Enclave.Sdk.Api`; NEVER change its format. CLI settings (default organisation and partner) go in `~/.enclave/cli.json`.
- Organisation: `--org` or `--org-id`, then `ENCLAVE_ORG` or `ENCLAVE_ORG_ID`, then the saved default. Partner: `--partner-id`, then `ENCLAVE_PARTNER_ID`, then the saved default.
- `enclave-cli commands [<command words>...]` describes every command, option, allowed value and error code as JSON, and MUST stay complete.

## Code
- .NET 10, nullable enabled, file-scoped namespaces, `_camelCase` private fields. Rules: `.editorconfig` (matches enclave.sdk.api).
- Analyzers: `AnalysisMode=AllEnabledByDefault` plus StyleCop; `TreatWarningsAsErrors=true`. Fix the cause. Suppress only with a comment at the suppression giving the reason.
- Package versions live only in `Directory.Packages.props`. Restore uses nuget.org only (`NuGet.config`). NEVER add a private feed: forks and CI build without credentials.
- `InvariantGlobalization` is on. Format and parse with `CultureInfo.InvariantCulture`.
- Read the clock and the local time zone only through `CliHost` (a `TimeProvider`), so tests can fix both. NEVER call `DateTime.Now`, `DateTimeOffset.Now` or `TimeZoneInfo.Local` from command code.
- Read and write files only through `CliHost.Files` (`Storage/IFileStore.cs`). NEVER call `File`, `Directory` or `FileStream` from command code: tests swap in an in-memory store, and only `DiskFileStore` touches the disk. Write the credentials file with `privateToUser: true`.
- Releases are self-contained single-file executables.
- Tests run the CLI as an ordinary .NET assembly, so they cannot catch a failure that exists only in the published binary. CI's smoke test (`.github/scripts/smoke-test.sh`) runs every published binary; extend it when a command depends on something the published form can break.

## Comments and documentation
Applies to code comments, AGENTS.md, README and every other document in this repository.
- Name packages and repositories exactly: `Enclave.Sdk.Api`, `Enclave.Sdk.Api.Data`, the sdk repository. NEVER call `Enclave.Sdk.Api` "the SDK".
- Plain, factual register; no intensifiers ("critically", "truly", "real").
- State things as they are. NEVER frame a statement as a contrast ("X, not Y", "rather than", "instead of") unless the alternative is the point of the sentence.
- Behaviour that depends on an external system (OS, API, library) cites its primary source: URL, spec section, or file and function, with the version it applies to.
- No status or timeline wording: "today", "currently", "until fixed".

Code comments also:
- Comments say WHY: the reason for the code, a constraint, a trade-off, or the external behaviour it relies on. NEVER write a comment that says WHAT the code below does; the code shows that. Delete such a comment when you find one.
- A trade-off states what was given up and why that is acceptable.
- NEVER delete a comment that gives a reason unless asked; correct a wrong one in place.
- XML docs only where a caller needs something the signature cannot show (for example, returns null when the file is missing): a short `<summary>`. No `<remarks>` essays.

## CI (`.github/workflows/ci.yml`)
- Pull request: the `test` job runs the tests (without the Pending category) once, on Linux. The `build` jobs publish and smoke test each RID on a runner of that OS and CPU, then upload archives (kept 2 days); they run no tests. No release.
- Linux binaries build on the runner and are smoke tested in a stock container of the oldest supported distribution: `almalinux:8` (RHEL 8, glibc 2.28) for glibc, `alpine:3.22` for musl. Keep these minimums in step with README's supported platforms table.
- Use only GitHub's own actions (`actions/*`).
- Push to main: the same, then the `release` job publishes GitHub release `v<version>` with eight archives and `SHA256SUMS`.
- Adding or removing a RID changes all of: the build matrices, the release job's expected file list, README's supported platforms table, and "RIDs shipped" above.
- Version: `YEAR.MONTH.DAY.RUN` in UTC, no leading zeros (e.g. `2026.10.5.57`), the scheme other Enclave products use. Pull requests add `-pr.<number>`. Computed once, in the `version` job.
- NEVER use `pull_request_target`. NEVER give a pull-request job a secret or write permission. Keep top-level `permissions: contents: read`; grant more per job.
- Reference actions by major version tag, e.g. `actions/checkout@v7`.
- Pass `${{ }}` values to `run:` scripts through `env:`.
- Lint workflow changes with actionlint.

## Git
- NEVER commit, push, tag, or open pull requests or releases. Leave changes in the working tree for the human to review.
