# CI (`.github/workflows/ci.yml`)

- Pull request: the `test` job runs the tests (without the Pending category) once, on Linux. The `build` jobs publish and smoke test each RID on a runner of that OS and CPU, then upload archives (kept 2 days); they run no tests. No release.
- Linux binaries build on the runner and are smoke tested in a stock container of the oldest supported distribution: `almalinux:8` (RHEL 8, glibc 2.28) for glibc, `alpine:3.22` for musl. Keep these minimums in step with README's supported platforms table. Neither image has jq or python.
- Use only GitHub's own actions (`actions/*`), referenced by major version tag, e.g. `actions/checkout@v7`.
- Push to main: the same, then the `release` job publishes GitHub release `v<version>` with eight archives and `SHA256SUMS`.
- Adding or removing a RID changes all of: the build matrices, the release job's expected file list, README's supported platforms table, and "RIDs shipped" in docs/agents/project.md.
- Version: semantic, from GitVersion (`GitVersion.yml`), as in enclave.sdk.api. It starts at 0.1.0; the CLI is not yet stable. Each merge to main releases the next patch; `+semver: minor` or `+semver: major` in a commit message takes a bigger step. Computed once, in the `version` job.
- NEVER use `pull_request_target`. NEVER give a pull-request job a secret or write permission. Keep top-level `permissions: contents: read`; grant more per job.
- Pass `${{ }}` values to `run:` scripts through `env:`.
- Lint workflow changes with actionlint.
