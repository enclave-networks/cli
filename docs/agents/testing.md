# Testing

- Run the CLI in-process through `CliRun` (`tests/Enclave.Cli.Tests/Support/`): `using var run = CliRun.Start(); var result = await run.RunAsync("system", "list");`. It gives the CLI its own environment, home directory, stdin, in-memory files (`run.Files`), a fake API and a fake partner API on separate addresses, and records exit code, stdout, stderr and every request.
- Tests never write to the disk: set up and check files through `run.Files`. A run that writes to the disk fails. Only `Storage/DiskFileStoreTests.cs` touches the disk, in its own temp directory, to prove `DiskFileStore` itself.
- A run that reads stdin while `StdinIsTerminal` is set fails.
- Fake the Enclave API with WireMock.Net through `CliRun` or `LoopbackApi`, which listen on 127.0.0.1 only. A listener on every interface raises a Windows Firewall prompt and accepts connections from other machines. enclave.sdk.api's own tests use WireMock.Net the same way.
- `CliRun.NetworkHandler` replaces the network beneath the CLI's own handler, for failures a fake API cannot give, such as a timeout. A run whose default partner API address (`DefaultPartnerApiUrl`) is not the fake's is refused unless it is set.
- Shared helpers live in `Support/`: `CliAssert` (outcomes), `CliResult` (stderr split into `Diagnostics` and the error), `JsonRead` and `JsonAssert` (JSON), `ApiJson` (API response bodies), `CliList` (list output), `TestData` (paths, IDs and the test time zone), `FixedTimeProvider` (clocks). Add a helper there when a second test file needs it; NEVER copy one into a test class.
- Assert the request the fake received (method, path, query, body) as well as the CLI's output. A test MUST fail if the CLI sends the wrong request, or none.
- NEVER call the live API. NEVER read the real `~/.enclave/` or user profile in tests; inject paths and environment.
- CI runs the tests once, on Linux, in the `test` job; the build jobs run the smoke test only. NEVER write a test whose result depends on the OS. NEVER depend on OS-specific behaviour (path separators, line endings, case sensitivity) without handling it.
- Test names: sentences joined with underscores that state the required behaviour, e.g. `Systems_list_prints_json_by_default`.
- Test names and comments state required behaviour. NEVER describe the code's current state: no "fails today", "the bug", "until fixed", "still", "currently".
