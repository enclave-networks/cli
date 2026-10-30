# cli

`enclave-cli` manages Enclave from the command line, through the Enclave Management API and the customer routes of the Enclave Partner API. It's built for programs that automate Enclave, such as AI agents, test infrastructure and GitHub Actions workflows: it prints JSON, exits with codes a program can branch on, and doesn't prompt.

To enrol a system, use the Enclave agent, `enclave`. "Left out" in [spec/cli-surface.md](spec/cli-surface.md#left-out) lists everything else the CLI leaves out.

## Install

Each release has 1 archive per platform and a `SHA256SUMS` file. The latest release's files have fixed URLs ([GitHub: Linking to releases](https://docs.github.com/repositories/releasing-projects-on-github/linking-to-releases)):

```text
https://github.com/enclave-networks/cli/releases/latest/download/<archive>
```

To pin a version, use its tag. [Releases](https://github.com/enclave-networks/cli/releases) lists the versions. Pin a version in CI and test runs, so every run gets the same binary.

```text
https://github.com/enclave-networks/cli/releases/download/v<version>/<archive>
```

| Platform | Archive |
|---|---|
| Windows x64 | `enclave-cli-win-x64.zip` |
| Windows arm64 | `enclave-cli-win-arm64.zip` |
| Linux x64 (glibc) | `enclave-cli-linux-x64.tar.gz` |
| Linux arm64 (glibc) | `enclave-cli-linux-arm64.tar.gz` |
| Linux x64 (musl, such as Alpine) | `enclave-cli-linux-musl-x64.tar.gz` |
| Linux arm64 (musl, such as Alpine) | `enclave-cli-linux-musl-arm64.tar.gz` |
| macOS x64 (Intel) | `enclave-cli-osx-x64.tar.gz` |
| macOS arm64 (Apple silicon) | `enclave-cli-osx-arm64.tar.gz` |

Each archive holds `LICENSE` and 1 self-contained executable, `enclave-cli` (`enclave-cli.exe` on Windows), which needs no .NET install.

### Linux and macOS (bash)

```bash
rid=linux-x64
base=https://github.com/enclave-networks/cli/releases/latest/download
# To pin a version: base=https://github.com/enclave-networks/cli/releases/download/v<version>

curl -fsSLO "$base/enclave-cli-$rid.tar.gz"
curl -fsSLO "$base/SHA256SUMS"
grep " enclave-cli-$rid.tar.gz\$" SHA256SUMS | sha256sum -c -

mkdir -p ~/.local/bin
tar -xzf "enclave-cli-$rid.tar.gz" -C ~/.local/bin enclave-cli
~/.local/bin/enclave-cli --version
```

`sha256sum -c -` prints `OK` for a matching archive, and exits 1 for any other. On macOS, use `shasum -a 256 -c -` in its place. Add `~/.local/bin` to your `PATH` if it isn't there.

### Windows (PowerShell)

```powershell
$rid = "win-x64"
$base = "https://github.com/enclave-networks/cli/releases/latest/download"
# To pin a version: $base = "https://github.com/enclave-networks/cli/releases/download/v<version>"

Invoke-WebRequest "$base/enclave-cli-$rid.zip" -OutFile "enclave-cli-$rid.zip"
Invoke-WebRequest "$base/SHA256SUMS" -OutFile SHA256SUMS
$expected = (Select-String -Path SHA256SUMS -Pattern " enclave-cli-$rid\.zip$").Line.Split(" ")[0]
if ((Get-FileHash "enclave-cli-$rid.zip" -Algorithm SHA256).Hash -ne $expected) {
    throw "enclave-cli-$rid.zip does not match SHA256SUMS"
}

$dir = "$env:LOCALAPPDATA\Programs\enclave-cli"
Expand-Archive "enclave-cli-$rid.zip" -DestinationPath $dir -Force
$env:Path = "$dir;$env:Path"
enclave-cli --version
```

The `$env:Path` change lasts for this session. To keep it, add the folder to your user `Path`.

## Supported platforms

| OS | Architectures | Minimum |
|---|---|---|
| Windows | x64, arm64 | Windows 10 1607, Windows Server 2012 |
| macOS | x64, arm64 | macOS 15 |
| Linux (glibc) | x64, arm64 | glibc 2.28 (RHEL 8 and newer) |
| Linux (musl) | x64, arm64 | Alpine 3.22, with libstdc++ |

On Alpine, .NET needs libstdc++ and libgcc (https://learn.microsoft.com/dotnet/core/install/linux-alpine#dependencies), which the stock `alpine:3.22` image does not include. `apk add libstdc++` installs both.

## GitHub Actions

This job installs a pinned version of `enclave-cli` on `ubuntu-latest` without sudo, then lists the organisation's connected systems. Later steps find `enclave-cli` through `$GITHUB_PATH`.

```yaml
jobs:
  enclave:
    runs-on: ubuntu-latest
    steps:
      - name: Install enclave-cli
        env:
          # Pinned, so every run gets the same binary. Releases lists the versions.
          ENCLAVE_CLI_VERSION: 0.1.0
        run: |
          cd "$RUNNER_TEMP"
          BASE_URL="https://github.com/enclave-networks/cli/releases/download/v$ENCLAVE_CLI_VERSION"
          curl -fsSLO "$BASE_URL/enclave-cli-linux-x64.tar.gz"
          curl -fsSLO "$BASE_URL/SHA256SUMS"
          grep ' enclave-cli-linux-x64.tar.gz$' SHA256SUMS | sha256sum -c -
          mkdir -p enclave-cli
          tar -xzf enclave-cli-linux-x64.tar.gz -C enclave-cli enclave-cli
          echo "$RUNNER_TEMP/enclave-cli" >> "$GITHUB_PATH"

      - name: List connected systems
        env:
          ENCLAVE_TOKEN: ${{ secrets.ENCLAVE_TOKEN }}
          ENCLAVE_ORG_ID: ${{ vars.ENCLAVE_ORG_ID }}
          # ENCLAVE_API_URL: https://staging-api.enclave.io
        run: enclave-cli system list --state connected
```

- Store the token as a secret. The organisation ID isn't secret, so a variable can hold it.
- To call staging, uncomment `ENCLAVE_API_URL`. `partner` commands also need `ENCLAVE_PARTNER_API_URL` (see [API address](#api-address)).
- On an Arm runner, such as `ubuntu-24.04-arm`, use `enclave-cli-linux-arm64.tar.gz`.

## Token

`enclave-cli` uses an Enclave personal access token. It reads the token from `ENCLAVE_TOKEN`, then from `~/.enclave/credentials.json`.

- **For an agent or a CI job,** set `ENCLAVE_TOKEN`. The token stays out of every file, and each process can have its own.
- **For a machine,** save the token once with `login`, which reads it from stdin:

  ```bash
  enclave-cli login --token-stdin < token.txt
  ```

  In PowerShell: `Get-Content token.txt | enclave-cli login --token-stdin`.

  `login` checks the token with a call that needs the `ReadOrgList` scope, saves it in `~/.enclave/credentials.json`, and prints the organisations the token can see. On Linux and macOS, only you can read the file (mode 0600). Other tools built on `Enclave.Sdk.Api` read the same file. `enclave-cli logout` deletes it; the token stays valid until you revoke it in the Enclave portal.

There's no `--token` option. Arguments end up in shell history, process listings, CI logs and agent transcripts, and personal access tokens don't expire.

Commands don't ask for confirmation, so give each token only the scopes its job needs.

## API address

`enclave-cli` calls production by default: `https://api.enclave.io`, and `https://partner-api.enclave.io` for `partner` commands. To call staging, set both addresses:

```bash
export ENCLAVE_API_URL=https://staging-api.enclave.io
export ENCLAVE_PARTNER_API_URL=https://staging-partner-api.enclave.io
```

Each address comes from its variable, then from `baseUrl` or `partnerApiBaseUrl` in `credentials.json`, then the production default. An empty variable counts as unset. `login` checks the token at the address in use and saves the addresses in use to `credentials.json`, so later commands on that machine use them without the variables. An address that isn't an absolute `http` or `https` URL exits 2 before any call, with an error that names where the address came from.

## Organisation and partner

A command acts within 1 organisation, chosen in this order:

1. `--org <name>` or `--org-id <orgId>`
2. `ENCLAVE_ORG` (a name) or `ENCLAVE_ORG_ID` (an ID)
3. The default saved by `org use`, in `~/.enclave/cli.json`

```bash
enclave-cli org use Acme
enclave-cli org use --id 3f2a9c1e-7b4d-4e8a-9c61-2d5b8e0f4a17
```

With none of these, the command uses the token's organisation when the token sees only 1. When it sees several, the command exits 2 `no_org` and lists their IDs and names in the error. `login` saves the default when the token sees 1 organisation.

The variables let each agent session or CI job fix its organisation without changing the saved default. An ID needs no lookup. A name, or no choice at all, adds 1 lookup call, which needs the token's `ReadOrgList` scope.

`partner` commands use a partner ID from `--partner-id`, then `ENCLAVE_PARTNER_ID`, then the default saved by `enclave-cli partner use --id <partnerId>`. The partner portal shows the ID.

`enclave-cli status` shows the token source, organisation and partner a command would use.

## Output and errors

`enclave-cli` writes JSON to stdout, with the API's field names. A list prints as `{ "kind": "system", "items": [...], "total": <n> }` once every page is read. Commands don't prompt or wait for input.

An error writes 1 indented JSON object to stderr: `{ "error": { "code", "status", "title", "detail", "errors" } }`. `code` comes from a fixed set, and `status`, `title`, `detail` and `errors` carry the API's problem details when the API sends them.

| Exit | `code` | Meaning |
|---|---|---|
| 0 | | Success |
| 1 | `api_error`, `not_implemented` | Any other API error, or a command with no handler |
| 2 | `invalid_argument`, `no_org`, `no_partner` | An argument or ID the CLI can't use, or no organisation or partner chosen |
| 3 | `token_missing`, `token_invalid` | No token, a token with a character no token holds, or HTTP 401 |
| 4 | `forbidden` | HTTP 403: the token lacks the scope |
| 5 | `not_found` | HTTP 404 on a command that names 1 item |
| 6 | `transient` | Network failure, timeout, HTTP 429 or 5xx; a retry can succeed |

Every run also writes diagnostics to stderr as plain text lines, before any error: where the token came from and the API address, how the organisation was chosen, each request's method and URL, the files `login`, `org use` and `partner use` saved, and, when a command fails, the .NET exception behind the error. They leave out the token. A diagnostic doesn't start with `{`, so the error starts at the first line that does.

Every command that changes something takes `--dry-run`. It prints the requests the command would send, without the Authorization header, sends none, and exits 0. Reads the change depends on, such as name lookups, run as usual, so a dry run needs a token.

```bash
enclave-cli policy delete "old vpn" --dry-run
```

`enclave-cli commands` describes every command, option, allowed value and error code as JSON, with no token and no call. `enclave-cli commands system list` describes 1 command.

[spec/cli-surface.md](spec/cli-surface.md) is the full reference: every command, flag and default, with examples.
