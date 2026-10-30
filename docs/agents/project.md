# Project

- Product: `enclave-cli`, a command-line front door to the Enclave Management APIs (https://api.enclave.io).
- Who it is for, and how that decides every input and output: `spec/cli-surface.md` "Who it is for".
- Public repository, MIT licence.
- `src/Enclave.Cli/`: the CLI. Assembly `enclave-cli`, namespace `Enclave.Cli`.
- `tests/Enclave.Cli.Tests/`: NUnit tests.
- `spec/cli-surface.md`: the specification of every command, flag, default and example.
- `.github/workflows/ci.yml`: build, test, package, release.
- Commands that control the local agent or machine belong to `enclave`, the Enclave agent (repository enclave-networks/fabric). NEVER add them here.
- `Enclave.Sdk.Api` is the Management API client package, built from repository enclave-networks/enclave.sdk.api. Repository enclave-networks/sdk is the agent's networking SDK and plays no part here.

- Build: `dotnet build enclave-cli.slnx -c Release`
- Test: `dotnet test enclave-cli.slnx -c Release`
- Test as CI does (leaves out the Pending category): `dotnet test enclave-cli.slnx -c Release --filter "TestCategory!=Pending"`
- RIDs shipped: `win-x64 win-arm64 linux-x64 linux-arm64 linux-musl-x64 linux-musl-arm64 osx-x64 osx-arm64`. Release binaries come from CI.
- Local builds are versioned `0.0.0-dev`; only CI sets a release version.
