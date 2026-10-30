# Architecture

- Every API call goes through the `Enclave.Sdk.Api` package. NEVER use `HttpClient` or build requests in this repository.
- An endpoint or field `Enclave.Sdk.Api` lacks: stop and report it. It is added to enclave-networks/enclave.sdk.api first, with tests there, then the `Enclave.Sdk.Api` version is bumped here. NEVER work around a gap locally.
- The API and `Enclave.Sdk.Api.Data` (built from the portal repository) do not change. Use only routes and fields the API has; a model `Enclave.Sdk.Api.Data` lacks or has wrong is defined or corrected in `Enclave.Sdk.Api`.
- The API is the security boundary: it decides what a token may read and change. NEVER add authorisation checks to the CLI, such as checking that an organisation or customer belongs to the caller, and NEVER describe a call the CLI makes (a lookup, a read for an exit code) as a security control. A gap in the API's own checks is reported to the API team.
- One command makes one `Enclave.Sdk.Api` call, with the exceptions listed in `spec/cli-surface.md` "Calls per command": paging, name lookups, reads that keep tags, labels and conditions, and bulk calls of 200 IDs.
- Command shape and naming: `spec/cli-surface.md` "Shape and naming".
- `spec/cli-surface.md` is the reference for every command, flag, default and example. A command or flag it does not list is not added without changing it first.
- Out of scope: enrolling a system (the agent does that), account password/2FA/accepting invites (browser flows), payment, and the partner's own properties, users and invites (personal access tokens cannot carry those scopes).
