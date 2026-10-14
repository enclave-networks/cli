# cli
A command-line client for the Enclave Management API and the customer routes of the Enclave Partner API. It does not enrol systems, which the Enclave agent `enclave` does, and it has no commands for account settings or accepting invites (browser sign-in flows), payment and billing, or the partner's own settings, users and invites (personal access tokens cannot carry the scopes they need). "Left out" in `proposed-cli-surface.md` lists each one.

## Supported platforms

| OS | Architectures | Minimum |
|---|---|---|
| Windows | x64, arm64 | Windows 10 1607, Windows Server 2012 |
| macOS | x64, arm64 | macOS 15 |
| Linux (glibc) | x64, arm64 | glibc 2.28 (RHEL 8 and newer) |
| Linux (musl) | x64, arm64 | Alpine 3.22, with libstdc++ |

On Alpine, .NET needs libstdc++ and libgcc (https://learn.microsoft.com/dotnet/core/install/linux-alpine#dependencies), which the stock `alpine:3.22` image does not include. `apk add libstdc++` installs both.
