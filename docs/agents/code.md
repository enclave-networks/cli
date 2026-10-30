# Code

- Style: `.editorconfig`. `global.json` requires SDK 10.0.102 or later, the first with runtime 10.0.2. Runtimes 10.0.0 and 10.0.1 have dotnet/runtime#121578, which can skip a caller's catch block when an exception is thrown from a `finally`, and a self-contained build ships the SDK's runtime.
- Warnings are errors, with every analyzer on and StyleCop. Fix the cause. Suppress only with a comment at the suppression giving the reason.
- Restore uses nuget.org only (`NuGet.config`). NEVER add a private feed: forks and CI build without credentials.
- NEVER write JSON by hand in a string, in src or tests. Build it with System.Text.Json (`JsonObject`, `JsonSerializer`) or, in tests, a `Support/` helper such as `ApiJson`.
- `InvariantGlobalization` is on. Format and parse with `CultureInfo.InvariantCulture`.
- Read the clock and the local time zone only through `CliHost` (a `TimeProvider`), so tests can fix both. NEVER call `DateTime.Now`, `DateTimeOffset.Now` or `TimeZoneInfo.Local` from command code.
- Read and write files only through `CliHost.Files` (`Storage/IFileStore.cs`). NEVER call `File`, `Directory` or `FileStream` from command code: tests swap in an in-memory store, and only `DiskFileStore` touches the disk. Write the credentials file with `privateToUser: true`.
- Releases are self-contained single-file executables, and tests run the CLI as an ordinary .NET assembly, so they cannot catch a failure that exists only in the published binary. CI's smoke test (`.github/scripts/smoke-test.sh`) runs every published binary; extend it when a command depends on something the published form can break.
