# Proposed CLI surface

`enclave-cli` drives the Enclave Management API and the Enclave Partner API through `Enclave.Sdk.Api` 1.0.4. AI agents are the main users; people use it too. This file lists the commands and the behaviour they share. Where it changes the CLI contract in AGENTS.md, the change is listed under "Changes to AGENTS.md".

## Shape and naming

`enclave-cli <noun> [<noun>] <verb> [arguments] [options]`

- A command is one or more nouns followed by one verb: `system list`, `dns zone delete`, `partner customer admin add`.
- Nouns are singular. Most commands act on one item (`policy delete <id>`), and singular reads correctly there. A plural name is accepted as a hidden alias (`systems list` runs `system list`), so a guess works; help and `commands` show the singular name only.
- Nouns use the same verbs wherever the API supports them: `list`, `show`, `create`, `update`, `delete`, `enable`, `disable`. Other verbs take the API's name: `revoke`, `approve`, `decline`, `convert`.
- A flag takes the name of the API model field it sets (`--approval-mode` sets `ApprovalMode`), so flags and `--from-file` JSON line up.

Where a command acts:

- Top-level nouns (`system` to `log`) act within the organisation in use. This is the common case, so it gets the shortest commands.
- `org` covers the organisation's own settings, users and invites.
- `partner` covers everything the partner owns, including its customers.

## Commands

```
enclave-cli
├── login --token-stdin                         check a personal access token and save it
├── logout                                      delete the saved token file
├── status                                      token source, organisation and partner in use, your role
├── org
│   ├── list
│   ├── use <orgId|name>
│   ├── show
│   ├── update
│   ├── user
│   │   ├── list
│   │   └── remove <accountId>
│   └── invite
│       ├── list
│       ├── send <email>
│       └── cancel <email>
├── partner                                     every partner command needs Enclave.Sdk.Api partner clients
│   ├── list                                    needs a portal change
│   ├── use <partnerId>
│   ├── show                                    needs a portal change
│   ├── update                                  needs a portal change
│   ├── user                                    needs a portal change
│   │   ├── list
│   │   ├── update <accountId>
│   │   └── remove <accountId>
│   ├── invite                                  needs a portal change
│   │   ├── list
│   │   ├── send <email>
│   │   ├── update <inviteId>
│   │   └── cancel <inviteId>
│   └── customer
│       ├── list
│       ├── show <customerId>
│       ├── create
│       ├── update <customerId>
│       ├── convert <customerId>                converts the customer to paid
│       ├── admin
│       │   ├── list <customerId>
│       │   ├── add <customerId> <accountId>
│       │   └── remove <customerId> <accountId>
│       ├── invite
│       │   ├── list <customerId>
│       │   ├── send <customerId> <email>
│       │   └── cancel <customerId> <inviteId>
│       └── auto-sync
│           ├── enable <customerId>
│           └── disable <customerId>
├── system
│   ├── list
│   ├── show <systemId>
│   ├── update <systemId>
│   ├── enable <systemId...>
│   ├── disable <systemId...>
│   └── revoke <systemId...>
├── pending                                     systems waiting for approval
│   ├── list
│   ├── show <systemId>
│   ├── update <systemId>
│   ├── approve <systemId...>
│   └── decline <systemId...>
├── key                                         enrolment keys
│   ├── list
│   ├── show <keyId>
│   ├── create
│   ├── update <keyId>
│   ├── enable <keyId...>
│   ├── disable <keyId...>
│   └── delete <keyId...>                       needs an Enclave.Sdk.Api change
├── policy
│   ├── list
│   ├── show <policyId>
│   ├── create
│   ├── update <policyId>
│   ├── enable <policyId...>
│   ├── disable <policyId...>
│   └── delete <policyId...>
├── tag
│   ├── list
│   ├── show <tag>
│   ├── create <tag>
│   ├── update <tag>
│   └── delete <tag...>
├── dns
│   ├── show                                    the organisation's DNS summary
│   ├── zone
│   │   ├── list
│   │   ├── show <zoneId>
│   │   ├── create <name>
│   │   ├── update <zoneId>
│   │   └── delete <zoneId>
│   └── record
│       ├── list
│       ├── show <recordId>
│       ├── create <name>
│       ├── update <recordId>
│       └── delete <recordId...>
├── trust                                       trust requirements
│   ├── list
│   ├── show <trustId>
│   ├── create
│   ├── update <trustId>
│   └── delete <trustId...>
├── log
│   └── list                                    organisation logs
└── commands [<noun>]                           every command, option, value and error code, as JSON
```

Partner customer commands take the customer ID first, so arguments run from parent to child.

## Command options

| Command | Options |
|---|---|
| `org update` | `--name`, `--website`, `--phone` |
| `system list` | `--search`, `--key <keyId>`, `--dns-name`, `--include-disabled`, `--sort` |
| `system update`, `pending update` | `--description`, `--notes`, `--set-tags a,b` |
| `system enable`, `key enable`, `policy enable` | `--until <when>`, `--expiry-action Disable\|Delete` |
| `pending list` | `--search`, `--key <keyId>`, `--sort` |
| `key list`, `policy list` | `--search`, `--include-disabled`, `--sort` |
| `key create` | `--description <text>` (required), `--type GeneralPurpose\|Ephemeral`, `--approval-mode Automatic\|Manual`, `--uses-remaining <n>`, `--tags a,b`, `--notes` |
| `key update` | `--description`, `--notes`, `--set-tags a,b` |
| `policy update`, `trust update` | `--description`, `--notes` |
| `policy create`, `trust create` | `--from-file` only |
| `tag list`, `trust list` | `--search`, `--sort` |
| `tag create` | `--colour`, `--notes` |
| `tag update` | `--name <new>`, `--colour`, `--notes` |
| `dns zone create` | `--notes` |
| `dns zone update` | `--name`, `--notes` |
| `dns record list` | `--zone <zoneId>`, `--search` |
| `dns record create` | `--zone <zoneId>` (required), `--type`, `--tags a,b`, `--systems id,id`, `--notes` |
| `dns record update` | `--name`, `--set-tags a,b`, `--set-systems id,id`, `--notes` |
| every `create` and `update` | `--from-file <path\|->`, `--template` |

Policies and trust requirements are created from a file only. Their content is nested (access rules, gateways, trust conditions), and flags can express only part of it. Partner and customer `create` and `update` take `--from-file`; their flags are chosen from the partner API models when `Enclave.Sdk.Api` has partner clients.

## Options on every command

| Option | Applies to | Meaning |
|---|---|---|
| `--org <orgId\|name>` | commands that act within an organisation | the organisation to use (see "Context") |
| `--partner <partnerId>` | `partner` commands | the partner to use (see "Context") |
| `-o json\|table\|id` | every command; `table` on `list` only | output format; `json` is the default |
| `--verbose` | every command | diagnostics on stderr |
| `--limit <n>`, `--all` | `list` | how many results, default 100; `--all` fetches every page |
| `--dry-run` | commands that change something through the API | prints the request and sends nothing |
| `--yes` | see "Confirmation" | confirms the change |

Option values that name an API enum (`--sort`, `--type`, `--approval-mode`, `--expiry-action`) take the API's names, the same strings JSON output shows, matched ignoring case. `commands` lists the allowed values.

The token comes from `ENCLAVE_TOKEN`, then `~/.enclave/credentials.json`. There is no `--token` option. Arguments end up in shell history, process listings, CI logs and agent transcripts, and personal access tokens do not expire (portal `Enclave.Accounts/Controllers/Api/TokensApiController.cs:105` issues them with `TimeSpan.MaxValue`).

## Context

The organisation and the partner are chosen separately, and both can be set at once. `partner` commands use the partner; every other command that acts within an organisation uses the organisation.

Organisation:

- Precedence: `--org`, then `ENCLAVE_ORG`, then the default that `org use` saves in `~/.enclave/cli.json`. `ENCLAVE_ORG` lets each agent session fix its organisation without writing the shared file.
- With none of these, the command looks up the token's organisations (one extra call). One organisation: it is used. Several: exit 2, with the `{ id, name }` list in the error so the caller can choose without another call.
- `login` saves the default when the token sees one organisation.
- Names match exactly, ignoring case. A name that matches more than one organisation exits 2 with the candidates.
- The default is saved as ID and name. Commands build the organisation client from the saved ID and make no lookup call.
- A lookup needs the token's `ReadOrgList` scope.

Partner:

- Precedence: `--partner`, then `ENCLAVE_PARTNER`, then the default that `partner use` saves in `~/.enclave/cli.json`.
- With none of these, the command exits 2 with an error naming `--partner` and `partner use`.
- The partner is given by ID. Looking partners up needs the `ReadPartnerList` scope, which personal access tokens cannot carry (see "Partner API").

## Several IDs

- `<id...>` takes 1 to 200 IDs. `-` reads newline-separated IDs from stdin; blank lines and `\r` are ignored and duplicates removed.
- A command that accepts several IDs always makes the bulk call, also for one ID, so its output is always `{ "requested": n, "affected": m }`. The bulk calls return a count only, and bulk approve succeeds when some IDs were not approved (portal `UnapprovedSystemsController.cs:170-177`). `affected` lower than `requested` means some IDs were unknown or already in that state; the exit code is 0, so a re-run after a timeout is safe.
- More than 200 IDs exits 2. The API's bulk limit is 200 (portal `Enclave.Utilities/HardLimits.cs:22`, `MaxBulkIds`).
- `-` with no IDs prints `{ "requested": 0, "affected": 0 }`, makes no call and exits 0, so a pipeline fed by an empty list succeeds.
- `-` when stdin is a terminal exits 2. The CLI never waits for input.
- `--until` takes one ID and prints the updated model; the API's timed enable has no bulk form. `dns zone delete` takes one ID; the API has no bulk zone delete (portal `DnsController.cs:145`).
- Single-ID commands (`show`, `update`, `--until`, `dns zone delete`, and the partner and customer commands) print the model and exit 5 for an unknown ID.

Example, approving every system enrolled with key 12:

```
enclave-cli pending list --key 12 --all -o id | enclave-cli pending approve - --yes
```

## ID checks

Every ID is checked before any call, and one bad ID exits 2. `Enclave.Sdk.Api` 1.0.4 puts IDs into URL paths unescaped (for example `UnapprovedSystemsClient.cs:95`), and .NET resolves `..` when it combines the path with the base address. `pending decline ../systems/ABCDE` would send `DELETE org/<id>/systems/ABCDE`, which revokes system ABCDE.

| ID | Format |
|---|---|
| system | letters and digits |
| tag | `^([a-z0-9]+[-.])*[a-z0-9]+$`, the API's tag rule (portal `TagValidationExtensions.cs:13`) |
| organisation, account, partner, customer | GUID; a customer ID is its organisation ID |
| key, policy, zone, record, trust requirement | integer (portal `Enclave.Configuration.Data/Identifiers`, `IdBackingType.Int`) |
| partner API invite | string (`OrganisationInviteId`, `IdBackingType.String`); the exact format is taken from the API when partner clients are added |

## Confirmation

These exit 6 without `--yes`:

- `delete`, `revoke`, `decline`: they cannot be undone.
- `org user remove`, `partner user remove`, `partner customer admin remove`: they remove a person's access.
- `pending approve`: it admits a machine to the network. It needs only the `CanWriteSystems` policy, the same as editing a pending system's description (portal `UnapprovedSystemsController.cs:128,158`), so a token's scopes cannot allow one without the other.
- `org invite send`, `partner invite send`, `partner customer invite send`: they give an outside email address access.
- `partner customer admin add`: it gives an account admin rights in the customer's organisation.
- `partner customer convert`: it converts the customer to paid (portal `CustomersController.cs:118`).
- `enable --until ... --expiry-action Delete`: it schedules a deletion.

`--dry-run` is checked first: with `--dry-run`, a missing `--yes` exits 0. The list stays short so that a permission rule asking a human to approve any command containing `--yes` stays useful.

## Dry run

`--dry-run` prints the request `Enclave.Sdk.Api` builds, with the organisation or partner, and sends nothing. The Authorization header is left out.

```
{ "dryRun": true, "org": { "id": "…", "name": "…" }, "request": { "method": "PUT", "url": "https://api.enclave.io/org/…/systems/disable", "body": { … } } }
```

Capturing the request needs an `Enclave.Sdk.Api` change (see below). Printing the captured request keeps URL building in one place and shows exactly what would be sent.

## Create and update

- `--from-file <path|->` takes the `Enclave.Sdk.Api` create or patch model as JSON. Unknown fields exit 2.
- In a patch file, fields present are set and absent fields are left as they are. `null` exits 2: the SDK's patch call rejects null (`Data/PatchClient.cs:32`).
- `create --template` prints the create model with every field present and each list holding one example entry.
- `update <id> --template` prints the resource's current values in patch-model shape (one GET), ready to edit and pass back. `show` output cannot be passed back as it is: for example `PolicyModel` returns sender tags as objects and `PolicyPatchModel` takes tag names.
- Flags and `--from-file` together exit 2.
- `--set-tags` and `--set-systems` replace the whole list, and `--set-tags ""` clears it. Tags decide policy membership, so the flag's name says it replaces.

## Output

- JSON by default, using the `Enclave.Sdk.Api` models unchanged.
- Lists: `{ "items": [...], "total": <n>, "truncated": <bool> }`. `-o id` and `-o table` drop the envelope, so when their output is truncated the CLI writes one line to stderr: `{ "warning": { "code": "truncated", "shown": 100, "total": 342 } }`.
- Enrolment key output includes the key's secret (`EnrolmentKeyModel.Key`). `-o id` leaves it out.

## Errors and exit codes

stderr carries one JSON object per error: `{ "error": { "code", "status", "title", "detail", "errors" } }`. `code` comes from a fixed set the CLI owns, listed by `commands`. `status`, `title`, `detail` and `errors` carry the API's problem details when the API sent them.

| Exit | `code` | Meaning |
|---|---|---|
| 0 | | success |
| 1 | `api_error` | any other API error |
| 2 | `invalid_argument`, `no_org`, `no_partner` | bad arguments, bad ID, no organisation or partner chosen |
| 3 | `token_missing`, `token_invalid` | no token, or HTTP 401 |
| 4 | `forbidden` | HTTP 403: the token lacks the scope |
| 5 | `not_found` | HTTP 404 on a single-ID command |
| 6 | `confirmation_required` | needs `--yes` |
| 7 | `transient` | network failure, timeout, HTTP 429 or 5xx: retrying can succeed |

Parse errors (unknown option, missing argument) follow the same rules: JSON on stderr, nothing on stdout, exit 2, with any suggestion in `detail`. System.CommandLine's default writes plain text and help and exits 1, so the CLI replaces its parse-error action.

`Enclave.Sdk.Api` throws `EnclaveApiException` only for `application/problem+json` responses (`Handlers/ProblemDetailsHttpMessageHandler.cs:20`). The CLI maps `HttpRequestException` status codes to the same exit codes, so a plain 401 or a proxy's 502 still gets 3 or 7.

## Login, logout and status

- `login --token-stdin` reads the token from stdin; with `ENCLAVE_TOKEN` set, `login` saves that token. It never prompts.
- `login` checks the token with one call (`GetOrganisationsAsync`, which needs the `ReadOrgList` scope), writes `~/.enclave/credentials.json`, and prints the organisations the token can see.
- `credentials.json` holds `personalAccessToken` and `baseUrl`, the format `Enclave.Sdk.Api` reads (`EnclaveClient.cs:97-114`). `login` keeps an existing `baseUrl`. On Linux and macOS it creates `~/.enclave` as 0700 and the file as 0600.
- The CLI reads `credentials.json` itself and passes `EnclaveClientOptions` to `Enclave.Sdk.Api`. Tests can then point it at a temporary directory, and `ENCLAVE_TOKEN` keeps the file's `baseUrl`.
- `logout` deletes `credentials.json`, prints its path, and says the token stays valid until it is revoked in the portal. Other tools built on `Enclave.Sdk.Api` read the same file and lose the token too.
- `status` makes one `GetOrganisationsAsync` call and prints where the token came from, the organisation and partner in use and where each choice came from, and your role in the organisation.
- No command prints the token, including under `--verbose` and `--dry-run`. A test runs every command that changes something with a known token and checks the token appears on neither stdout nor stderr.

## Calls per command

One `Enclave.Sdk.Api` call per command, with these exceptions:

- `--all`, or `--limit` above 200, makes one call per page. The API returns at most 200 per page.
- Looking up an organisation by name, or with no organisation chosen, adds one call.

## Partner API

The partner API is a separate service (portal `src/Enclave.Partner.Api`) with every route under `/partner/{partnerId}/`. It covers the partner (properties, users, invites) and its customers (properties, admins, invites, auto-sync).

- It runs on its own host: `PartnerApiUrl`, `http://partner-api.local:8083` in development (portal `Enclave.Partner.Portal.Server/appsettings.Development.json:5`) and set at deployment in production. `Enclave.Sdk.Api` needs partner clients and a partner base URL; `credentials.json` holds one `baseUrl`.
- Personal access tokens can carry the partner scopes `ReadCustomers` and `WriteCustomers`, and no other partner scope (portal `Enclave.Accounts/Config/EnclaveIdentityClientConfig.cs:195-216`). The partner API requires the scope claim on the token (portal `Enclave.Api.Scaffolding/Authorisation/AuthorizationExtensions.cs:38`). With a personal access token:
  - `partner customer` commands work.
  - `partner list`, `show`, `update`, `user` and `invite` need `ReadPartnerList`, `ReadPartnerInfo` or `WritePartnerSettings` (portal `Enclave.Partner.Api/WebStartup.cs:115-121`), which need adding to the personal access token client.
  - The partner ID cannot be looked up, so `partner use` and `--partner` take an ID.
- A customer's ID is its organisation ID: the partner API parses `customerId` as an `OrganisationGuid` (portal `CustomersController.cs:69`). Partner staff work on a customer's systems, policies and the rest through the main API, where `org list` includes the customer's organisation with `PartnerAccess` set:

  ```
  enclave-cli partner customer list -o id
  enclave-cli system list --org <customerId>
  ```

- `org invite cancel` takes an email address, which is what `Enclave.Sdk.Api` accepts (`CancelInviteAync(email)`). The partner and customer `invite cancel` take an invite ID, which is what the partner API's routes take. Making them match needs an API change.

## Left out

- Enrolling a system (`IAuthorityClient.EnrolAsync`): the agent `enclave` does that.
- Account settings (password, 2FA) and accepting organisation or partner invites: these are browser sign-in flows.
- Payment, and the partner API's Gradient billing endpoints.
- The partner API's `countries` and `referlink` (data for the partner portal's screens) and `customer oldest-version`.

## Needs `Enclave.Sdk.Api` changes

Each is added in enclave-networks/enclave.sdk.api first, with tests there, then used here.

1. Escape IDs in URL paths.
2. An HTTP handler option on `EnclaveClientOptions`, for `--dry-run`.
3. Apply a whole patch model, and allow `null` to clear a field, for `update --from-file`.
4. Enrolment key delete, single and bulk, for `key delete`. The API has both (portal `EnrolmentKeysController.cs:265,295`).
5. `CreateOrganisationClient(OrganisationGuid)`. `CreateOrganisationClient` takes an `AccountOrganisationModel` and uses only its `OrgId` (`OrganisationClient.cs:25`), so building one from a saved ID means filling the role and partner-access fields with placeholders.
6. Status checks on calls that pass a failure through as success when the response is not problem+json: `RemoveUserAsync`, `InviteUserAsync`, `CancelInviteAync` (`OrganisationClient.cs:91-133`) and the single create, enable and disable calls.
7. The `hostname` filter on DNS record list. The API accepts it (portal `DnsRecordsRequestModel.cs:18`).
8. The `meta/search-keys` endpoints, so `commands` can describe `--search`. These also need Enclave.Sdk.Api.Data changes.
9. Partner API clients (partner, users, invites, customers, customer admins, customer invites, auto-sync) and a partner API base URL.

## Needs portal changes

- Add `ReadPartnerList`, `ReadPartnerInfo` and `WritePartnerSettings` to the personal access token client's allowed scopes, for the `partner` commands outside `partner customer`.
- Organisation update, user removal and invites need organisation membership and no token scope (portal `OrganisationController.cs:49,91,132`), so a token limited to read scopes can still do them.

## Changes to AGENTS.md

If this proposal is accepted, AGENTS.md changes to match:

- Command shape: `<noun> [<noun>] <verb>`, singular nouns, plural accepted as a hidden alias. The noun list becomes `org`, `partner`, `system`, `pending`, `key`, `policy`, `tag`, `dns zone`, `dns record`, `trust`, `log`.
- Token sources: `ENCLAVE_TOKEN`, then `credentials.json`. `--token` is removed.
- Organisation and partner precedence, including `ENCLAVE_ORG` and `ENCLAVE_PARTNER`.
- The `--yes` list in "Confirmation", and `--dry-run` taking precedence over it.
- Exit 7, and the fixed `code` set.
- The truncation warning on stderr.
- The bulk output shape, empty stdin, the 200-ID limit and the ID checks.
- `commands [<noun>]` replaces `commands --json`.

## Build order

1. `login`, `status`, `org list`, `org use`: the token, context, output, error and exit-code handling every later command uses.
2. `list` and `show` for every top-level noun.
3. Changes, with `--dry-run` and `--yes`: `system`, `pending`, `key`, `policy`, `tag`.
4. `dns`, `trust`, `org update`, `org user`, `org invite`, and the commands that wait on `Enclave.Sdk.Api` changes.
5. `partner customer`, once `Enclave.Sdk.Api` has partner clients; the rest of `partner` once the portal change is made.
