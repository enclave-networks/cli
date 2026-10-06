# Proposed CLI surface

`enclave-cli` drives the Enclave Management API and the Enclave Partner API through `Enclave.Sdk.Api` 1.0.4. AI agents are the main users; people use it too. This file lists the commands and the behaviour they share. Where it changes the CLI contract in AGENTS.md, the change is listed under "Changes to AGENTS.md".

## Shape and naming

`enclave-cli <noun> <verb> [arguments] [options]`

- A command is a noun and a verb: `system list`, `policy create`. A noun's own parts take a hyphenated verb in place of another level: `dns create-hostname`, `org remove-user`, `partner customer add-admin`. `partner customer` is the one second-level noun, since customers have a dozen verbs of their own.
- Nouns are singular. Most commands act on one item (`policy delete <id>`), and singular reads correctly there. A plural name is accepted as a hidden alias (`systems list` runs `system list`), so a guess works; help and `commands` show the singular name only.
- Nouns use the same verbs wherever the API supports them: `list`, `show`, `create`, `update`, `delete`, `enable`, `disable`. Other verbs take the API's name: `revoke`, `approve`, `decline`, `convert`, `invite`.
- A flag is named after the API model field it sets (`--approval-mode` sets `ApprovalMode`), shortened where the field name is long: `--senders` sets `SenderTags`, `--receivers` sets `ReceiverTags`, `--trust` sets `SenderTrustRequirements`.

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
│   ├── use <name> | --id <orgId>
│   ├── show
│   ├── update
│   ├── list-users
│   ├── remove-user <email> | --id <accountId>
│   ├── list-invites
│   ├── invite <email>
│   └── cancel-invite <email>
├── partner                                     every partner command needs Enclave.Sdk.Api partner clients
│   ├── list                                    needs a portal change
│   ├── use --id <partnerId>
│   ├── show                                    needs a portal change
│   ├── update                                  needs a portal change
│   ├── list-users                              needs a portal change
│   ├── update-user --id <accountId>            needs a portal change
│   ├── remove-user --id <accountId>            needs a portal change
│   ├── list-invites                            needs a portal change
│   ├── invite <email>                          needs a portal change
│   ├── update-invite --id <inviteId>           needs a portal change
│   ├── cancel-invite --id <inviteId>           needs a portal change
│   └── customer
│       ├── list
│       ├── show <customer>
│       ├── create
│       ├── update <customer>
│       ├── convert <customer>                  converts the customer to paid
│       ├── list-admins <customer>
│       ├── add-admin <customer> --user <email> | --user-id <accountId>
│       ├── remove-admin <customer> --user <email> | --user-id <accountId>
│       ├── list-invites <customer>
│       ├── invite <customer> --email <email>
│       ├── cancel-invite <customer> --invite <inviteId>
│       ├── enable-auto-sync <customer>
│       └── disable-auto-sync <customer>
├── system                                      --pending: systems waiting for approval
│   ├── list
│   ├── show <systemId>
│   ├── update <systemId>
│   ├── approve <systemId...>
│   ├── decline <systemId...>
│   ├── enable <systemId...>
│   ├── disable <systemId...>
│   └── revoke <systemId...>
├── key                                         enrolment keys
│   ├── list
│   ├── show <key>
│   ├── create
│   ├── update <key>
│   ├── enable <key...>
│   ├── disable <key...>
│   └── delete <key...>                         needs an Enclave.Sdk.Api change
├── policy
│   ├── list
│   ├── show <policy>
│   ├── create
│   ├── update <policy>
│   ├── enable <policy...>
│   ├── disable <policy...>
│   └── delete <policy...>
├── tag
│   ├── list
│   ├── show <tag>
│   ├── set <tag>
│   └── delete <tag...>
├── dns
│   ├── show                                    the organisation's DNS summary
│   ├── list-zones
│   ├── show-zone <zone>
│   ├── create-zone <zone>
│   ├── update-zone <zone>
│   ├── delete-zone <zone>
│   ├── list-hostnames
│   ├── show-hostname <hostname>
│   ├── create-hostname <hostname>
│   ├── update-hostname <hostname>
│   └── delete-hostname <hostname...>
├── trust                                       trust requirements
│   ├── list
│   ├── show <trust>
│   ├── create
│   ├── update <trust>
│   └── delete <trust...>
├── log                                         the organisation's activity log
└── commands [<noun> [<verb>]]                  every command, option, value and error code, as JSON
```

- A customer is an organisation. `<customer>` is its name, or `--org-id <orgId>` gives its organisation ID, the only ID a customer has (portal `CustomersController.cs:69` reads the customer ID as an `OrganisationGuid`). Anything else a partner customer command needs is a named option (`--user`, `--user-id`, `--email`, `--invite`).
- An ID is never a positional argument. `--id` gives the ID of the command's own item. Every other item has a pair of options: `--<item>` takes its name (an email address for a user) and `--<item>-id` takes its ID: `--org`/`--org-id`, `--user`/`--user-id`, `--key`/`--key-id`, `--zone`/`--zone-id`, `--trust`/`--trust-id`, and `--partner-id` (partners are given by ID only). The option decides how its value is read, and the CLI never inspects a value to guess its type: `--key 12` is a key described "12", `--key-id 12` is key 12, and `--user-id` takes only a GUID. Nothing in `42` or a GUID says what it identifies, so the option name does. Positional arguments are names, email addresses, hostnames, tag names, and system IDs, since systems have no names.
- `<key>`, `<policy>`, `<trust>`, `<zone>` and `<hostname>` are names: a description, a zone's name, a full hostname. `--id 42` gives the ID instead, `--id 42,43,57` several, and `--id -` reads IDs from stdin.
- `--pending` on `system list`, `show` and `update` acts on systems waiting for approval, which the API keeps apart from enrolled systems (`unapproved-systems`). `approve` and `decline` act on those systems only.
- A hostname is written in full, `db.internal`, and its zone is the zone its name ends in. Zones are named, `internal`; IDs work too.

## Command options

| Command | Options |
|---|---|
| `org update` | `--name`, `--website`, `--phone` |
| `system list` | `--search <text>`, `--tag a,b`, `--state connected\|disconnected`, `--os windows\|linux\|mac`, `--type general\|ephemeral`, `--gateway`, `--key <name>`, `--key-id <id>`, `--dns-name`, `--not-seen-for <duration>`, `--include-disabled`, `--sort` |
| `system update` | `--description`, `--notes`, `--set-tags a,b`, `--add-tags a,b`, `--remove-tags a,b`, `--enable-gateway-for <subnet>,...`, `--disable-gateway` |
| `system enable`, `key enable`, `policy enable` | `--until <when>`, `--expiry-action disable\|delete` |
| `system list --pending` | `--search <text>`, `--tag a,b`, `--key <name>`, `--key-id <id>`, `--waiting-for <duration>`, `--sort` |
| `system update --pending` | `--description`, `--notes`, `--set-tags a,b`, `--add-tags a,b`, `--remove-tags a,b` |
| `key list` | `--search <text>`, `--tag a,b`, `--approval automatic\|manual`, `--state enabled\|disabled\|no-uses`, `--include-disabled`, `--sort` |
| `key create` | `--description <text>` (required), `--type general\|ephemeral`, `--approval-mode automatic\|manual`, `--uses-remaining <n>`, `--tags a,b`, `--allow-ip <range>` (repeatable), `--keep-disconnected <duration>`, `--until <when>`, `--expiry-action disable\|delete`, `--notes` |
| `key update` | `--description`, `--notes`, `--approval-mode`, `--uses-remaining <n>`, `--set-tags a,b`, `--add-tags a,b`, `--remove-tags a,b`, `--set-allow-ip <range>` (repeatable), `--keep-disconnected <duration>` |
| `policy list` | `--search <text>`, `--tag a,b`, `--state enabled\|disabled`, `--include-disabled`, `--sort` |
| `policy create` | `--description <text>` (required), `--senders a,b`, `--receivers a,b`, `--acl <protocol>[:<ports>]` (repeatable), `--trust <name>,...`, `--trust-id <id>,...`, `--gateway <systemId>:<route>,...` (repeatable), `--allow-ip <range>` (repeatable), `--active-hours <hours>`, `--until <when>`, `--expiry-action disable\|delete`, `--notes`, `--disabled` |
| `policy update` | `--description`, `--notes`, `--set-senders a,b`, `--set-receivers a,b`, `--set-acl <protocol>[:<ports>]` (repeatable), `--set-trust <name>,...`, `--set-trust-id <id>,...`, `--set-gateway <systemId>:<route>,...` (repeatable), `--set-allow-ip <range>` (repeatable), `--set-active-hours <hours>` |
| `trust list` | `--search <text>`, `--type user-auth\|public-ip`, `--sort` |
| `trust create` | `--description <text>` (required), `--type user-auth\|public-ip` (required), `--notes`; for `user-auth`: `--authority portal\|azure\|google\|okta\|jumpcloud\|duo\|oidc`, `--tenant <id>` (azure), `--authority-uri <url>` (oidc), `--claim <claim>=<value>` (repeatable); for `public-ip`: `--allow-ip <range>`, `--block-ip <range>`, `--allow-country <code>`, `--block-country <code>` (each repeatable) |
| `trust update` | `--description`, `--notes`, and the create flags for its type with a `--set-` prefix (`--set-claim`, `--set-allow-ip`, ...), which replace all its conditions |
| `tag list` | `--search <text>`, `--sort` |
| `tag set` | `--name <new>`, `--colour`, `--trust <name>,...`, `--trust-id <id>,...`, `--notes` |
| `dns create-zone` | `--auto-dns-tags a,b`, `--notes` |
| `dns update-zone` | `--name`, `--set-auto-dns-tags a,b`, `--notes` |
| `dns list-hostnames` | `--zone <name>`, `--zone-id <id>`, `--search <text>` |
| `dns create-hostname` | `--type`, `--tags a,b`, `--systems id,id`, `--notes` |
| `dns update-hostname` | `--name`, `--set-tags a,b`, `--add-tags a,b`, `--remove-tags a,b`, `--set-systems id,id`, `--notes` |
| `log` | `--since <when>`, `--until <when>`, `--user <email>` (needs an `Enclave.Sdk.Api` change) |

- `--acl` takes a protocol (`any`, `tcp`, `udp`, `icmp`) and, for TCP and UDP, a port or range: `tcp:5432`, `udp:53`, `tcp:8000-8100`. A policy created with no `--acl` allows any protocol.
- `--gateway GW001:10.0.0.0/16,10.1.0.0/16` makes a gateway policy: the senders reach those routes through system GW001. `--allow-ip` narrows the addresses they may reach through it. `--gateway` and `--receivers` together exit 2. Gateway priority and traffic direction take the API's defaults; flags for them are added once their values are confirmed from the API.
- `--enable-gateway-for 10.0.0.0/16,10.1.0.0/16` makes the system a gateway for those subnets, replacing any it had; `--disable-gateway` stops it acting as one.
- `--active-hours "mon-fri 08:00-18:00 Europe/London"` takes days, a start and end time, and an IANA time zone (UTC when left out). `--set-active-hours ""` removes the restriction.
- `--keep-disconnected 30m` keeps systems enrolled with the key for that long after they disconnect.
- `--until` and `--expiry-action` on `create` set the item to switch itself off or delete itself, as `enable --until` does.
- `--claim groups=<object id>` requires that claim in the user's sign-in token. Countries are ISO 3166 two-letter codes.
- `tag set <tag>` updates the tag, and creates it when it does not exist: the API has separate create and update calls, and to a user both mean "make tag `web` look like this". `--trust` replaces the tag's trust requirements. `--name` renames, so the tag must exist.
- Flags for partner and customer `create` and `update` are chosen from the partner API models when `Enclave.Sdk.Api` has partner clients.

## Options on every command

| Option | Applies to | Meaning |
|---|---|---|
| `--org <name>`, `--org-id <orgId>` | commands that act within an organisation | the organisation to use (see "Context") |
| `--partner-id <partnerId>` | `partner` commands | the partner to use (see "Context") |
| `-o json\|table\|id\|count\|secret` | every command; `table` and `count` on lists (`list`, `list-*`, `log`) only; `secret` on `key create` and `key show` only | output format; `json` is the default |
| `--verbose` | every command | diagnostics on stderr |
| `--limit <n>`, `--all` | lists (`list`, `list-*`, `log`) | how many results, default 100; `--all` fetches every page |
| `--dry-run` | commands that change something through the API | prints the request and sends nothing |

Option values that name an API enum (`--sort`, `--type`, `--approval-mode`, `--expiry-action`, `--state`, `--os`) take lower-case, hyphenated names (`recently-connected`, `general`, `automatic`, `disable`), matched ignoring case. JSON output keeps the API's own names. `commands` lists the allowed values.

The token comes from `ENCLAVE_TOKEN`, then `~/.enclave/credentials.json`. There is no `--token` option. Arguments end up in shell history, process listings, CI logs and agent transcripts, and personal access tokens do not expire (portal `Enclave.Accounts/Controllers/Api/TokensApiController.cs:105` issues them with `TimeSpan.MaxValue`).

## Context

The organisation and the partner are chosen separately, and both can be set at once. `partner` commands use the partner; every other command that acts within an organisation uses the organisation.

Organisation:

- Precedence: `--org` or `--org-id`, then `ENCLAVE_ORG` (a name) or `ENCLAVE_ORG_ID` (an ID), then the default that `org use` saves in `~/.enclave/cli.json`. The environment variables let each agent session fix its organisation without writing the shared file.
- With none of these, the command looks up the token's organisations (one extra call). One organisation: it is used. Several: exit 2, with the `{ id, name }` list in the error so the caller can choose without another call.
- `login` saves the default when the token sees one organisation.
- Names match exactly, ignoring case. A name that matches more than one organisation exits 2 with the candidates.
- The default is saved as ID and name. Commands build the organisation client from the saved ID and make no lookup call.
- A lookup needs the token's `ReadOrgList` scope.

Partner:

- Precedence: `--partner-id`, then `ENCLAVE_PARTNER_ID`, then the default that `partner use` saves in `~/.enclave/cli.json`.
- With none of these, the command exits 2 with an error naming `--partner-id` and `partner use`.
- The partner is given by ID. Looking partners up needs the `ReadPartnerList` scope, which personal access tokens cannot carry (see "Partner API").

## Several IDs

- A command that takes several items takes 1 to 200: system IDs or names as arguments, or IDs as `--id 42,43,57`. `-` in place of the arguments, or `--id -`, reads newline-separated IDs from stdin; blank lines and `\r` are ignored and duplicates removed.
- A command that accepts several IDs always makes the bulk call, also for one ID, so its output is always `{ "requested": n, "affected": m }`. The bulk calls return a count only, and bulk approve succeeds when some IDs were not approved (portal `UnapprovedSystemsController.cs:170-177`). `affected` lower than `requested` means some IDs were unknown or already in that state; the exit code is 0, so a re-run after a timeout is safe.
- More than 200 IDs exits 2. The API's bulk limit is 200 (portal `Enclave.Utilities/HardLimits.cs:22`, `MaxBulkIds`).
- `-` with no IDs prints `{ "requested": 0, "affected": 0 }`, makes no call and exits 0, so a pipeline fed by an empty list succeeds.
- `-` when stdin is a terminal exits 2. The CLI never waits for input.
- `--until` takes one ID and prints the updated model; the API's timed enable has no bulk form. `dns delete-zone` takes one zone; the API has no bulk zone delete (portal `DnsController.cs:145`).
- Single-ID commands (`show`, `update`, `--until`, `dns delete-zone`, and the partner and customer commands) print the model and exit 5 for an unknown ID.

Example, approving every system enrolled with key 12:

```
enclave-cli system list --pending --key-id 12 --all -o id | enclave-cli system approve -
```

## Filters

`list` commands take `--search <text>` for free text, and one flag per search key the API defines for that resource (portal `Enclave.Configuration.Data/Modules/*/*SearchKeyService.cs`): for `system list`, `--tag`, `--state`, `--os`, `--type`, `--gateway` and `--key`. The CLI writes them into the API's search query, and they combine: `system list --tag web --state connected` lists connected systems tagged `web`. `--state disabled` lists disabled items without `--include-disabled`.

`system list --not-seen-for 90d` and `system list --pending --waiting-for 7d` have no API search key, so the CLI reads every page and keeps the matches; `total` counts the matches.

## Names and IDs

Keys, policies, DNS zones, hostnames and trust requirements are given as arguments by name: the description of a key, policy or trust requirement, the name of a zone, or the full name of a hostname (`db.internal`). Names are looked up with one list call, and each must match exactly one item, ignoring case; no match, or several, exits 2 with the candidates. `--id` gives IDs instead and makes no lookup; IDs read from stdin are IDs, since they come from `-o id`. Options that point at another item come in pairs, one for its name and one for its ID (`--key`/`--key-id`). Systems take IDs only: hostnames are not unique. Tags are given by name only. `org remove-user` takes an email address in place of the account ID, looked up the same way. A partner customer is given by its name, looked up in the partner's customer list, or by `--org-id`. On `partner customer add-admin` and `remove-admin`, `--user` takes a partner user's email address and `--user-id` an account ID; the email lookup reads the partner's users, which needs the portal change in "Needs portal changes".

## ID checks

Every ID is checked before any call, and one bad ID exits 2. `Enclave.Sdk.Api` 1.0.4 puts IDs into URL paths unescaped (for example `UnapprovedSystemsClient.cs:95`), and .NET resolves `..` when it combines the path with the base address. `system decline ../systems/ABCDE` would send `DELETE org/<id>/systems/ABCDE`, which revokes system ABCDE.

| ID | Format |
|---|---|
| system | letters and digits |
| tag | `^([a-z0-9]+[-.])*[a-z0-9]+$`, the API's tag rule (portal `TagValidationExtensions.cs:13`); tags are given by name only |
| organisation (a partner's customers included), account, partner | GUID |
| key, policy, zone, hostname, trust requirement | integer, after `--id` or an option naming the item (portal `Enclave.Configuration.Data/Identifiers`, `IdBackingType.Int`). A name goes to the lookup and never into a URL path |
| partner API invite | string (`OrganisationInviteId`, `IdBackingType.String`); the exact format is taken from the API when partner clients are added |

## Changes run when given

No command asks for confirmation; `--dry-run` shows the request before it is sent. The trade-off is that a mistaken `revoke` or `delete` runs, so an agent's token should carry only the scopes it needs (`RequestedScopes` when the token is created). Approving a waiting system needs only the `CanWriteSystems` policy, the same as editing its description (portal `UnapprovedSystemsController.cs:128,158`), so a token that can edit systems can also admit them.

## Dry run

`--dry-run` prints the request `Enclave.Sdk.Api` builds, with the organisation or partner, and sends nothing. The Authorization header is left out.

```
{ "dryRun": true, "org": { "id": "…", "name": "…" }, "request": { "method": "PUT", "url": "https://api.enclave.io/org/…/systems/disable", "body": { … } } }
```

Capturing the request needs an `Enclave.Sdk.Api` change (see below). Printing the captured request keeps URL building in one place and shows exactly what would be sent.

## Create and update

- Every field is set with a flag. An update sends only the fields given; the rest are left as they are.
- `--set-tags` and `--set-systems` replace the whole list, and `--set-tags ""` clears it. Tags decide policy membership, so the flag's name says it replaces.
- `--add-tags` and `--remove-tags` change the list in place: the CLI reads the item, then patches the whole list. A change someone else makes between the two calls is overwritten; `--set-tags` gives an exact list.

## Output

- JSON by default, using the `Enclave.Sdk.Api` models unchanged.
- Lists: `{ "items": [...], "total": <n>, "truncated": <bool> }`. `-o id` and `-o table` drop the envelope, so when their output is truncated the CLI writes one line to stderr: `{ "warning": { "code": "truncated", "shown": 100, "total": 342 } }`.
- Enrolment key output includes the key's secret (`EnrolmentKeyModel.Key`). `-o id` leaves it out; `-o secret` prints the secret alone.
- `-o count` prints a list's `total` alone.
- `commands system list` describes one command, its options and their allowed values.
- Common questions have a filter or an output format, so a script needs `jq` only for something unusual.

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
| 6 | `transient` | network failure, timeout, HTTP 429 or 5xx: retrying can succeed |

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
- A key, policy, zone or trust requirement given by name adds one call.
- `--add-tags` and `--remove-tags` read the item first, which adds one call.
- `tag set` on a tag that does not exist makes a second call to create it.
- `--not-seen-for` and `--waiting-for` read every page.

## Partner API

The partner API is a separate service (portal `src/Enclave.Partner.Api`) with every route under `/partner/{partnerId}/`. It covers the partner (properties, users, invites) and its customers (properties, admins, invites, auto-sync).

- It runs on its own host: `PartnerApiUrl`, `http://partner-api.local:8083` in development (portal `Enclave.Partner.Portal.Server/appsettings.Development.json:5`) and set at deployment in production. `Enclave.Sdk.Api` needs partner clients and a partner base URL; `credentials.json` holds one `baseUrl`.
- Personal access tokens can carry the partner scopes `ReadCustomers` and `WriteCustomers`, and no other partner scope (portal `Enclave.Accounts/Config/EnclaveIdentityClientConfig.cs:195-216`). The partner API requires the scope claim on the token (portal `Enclave.Api.Scaffolding/Authorisation/AuthorizationExtensions.cs:38`). With a personal access token:
  - `partner customer` commands work.
  - `partner list`, `show`, `update`, `user` and `invite` need `ReadPartnerList`, `ReadPartnerInfo` or `WritePartnerSettings` (portal `Enclave.Partner.Api/WebStartup.cs:115-121`), which need adding to the personal access token client.
  - The partner ID cannot be looked up, so `partner use` and `--partner-id` take an ID.
- A customer is an organisation, and the partner API identifies it by its organisation ID (portal `CustomersController.cs:69` parses the route's customer ID as an `OrganisationGuid`). Partner staff work on a customer's systems, policies and the rest through the main API, where `org list` includes the customer's organisation with `PartnerAccess` set:

  ```
  enclave-cli partner customer list -o id
  enclave-cli system list --org-id <orgId>
  ```

- `org cancel-invite` takes an email address, which is what `Enclave.Sdk.Api` accepts (`CancelInviteAync(email)`). The partner and customer `cancel-invite` take an invite ID, which is what the partner API's routes take. Making them match needs an API change.

## Left out

- Enrolling a system (`IAuthorityClient.EnrolAsync`): the agent `enclave` does that.
- Account settings (password, 2FA) and accepting organisation or partner invites: these are browser sign-in flows.
- Payment, and the partner API's Gradient billing endpoints.
- The partner API's `countries` and `referlink` (data for the partner portal's screens) and `customer oldest-version`.

## Needs `Enclave.Sdk.Api` changes

Each is added in enclave-networks/enclave.sdk.api first, with tests there, then used here.

1. Escape IDs in URL paths.
2. An HTTP handler option on `EnclaveClientOptions`, for `--dry-run`.
3. Allow `null` in a patch, so an update flag given an empty value (`--set-active-hours ""`) can clear a field; `PatchClient.Set` rejects null (`Data/PatchClient.cs:32`).
4. Enrolment key delete, single and bulk, for `key delete`. The API has both (portal `EnrolmentKeysController.cs:265,295`).
5. `CreateOrganisationClient(OrganisationGuid)`. `CreateOrganisationClient` takes an `AccountOrganisationModel` and uses only its `OrgId` (`OrganisationClient.cs:25`), so building one from a saved ID means filling the role and partner-access fields with placeholders.
6. Status checks on calls that pass a failure through as success when the response is not problem+json: `RemoveUserAsync`, `InviteUserAsync`, `CancelInviteAync` (`OrganisationClient.cs:91-133`) and the single create, enable and disable calls.
7. The `hostname` filter on DNS record list. The API accepts it (portal `DnsRecordsRequestModel.cs:18`).
8. The `meta/search-keys` endpoints, so `commands` can describe `--search`. These also need Enclave.Sdk.Api.Data changes.
9. Partner API clients (partner, users, invites, customers, customer admins, customer invites, auto-sync) and a partner API base URL.
10. Filters on the activity log, for `log --since`, `--until` and `--user`. `GetLogsAsync` takes only a page and page size; whether the API filters logs needs checking.

## Needs portal changes

- Add `ReadPartnerList`, `ReadPartnerInfo` and `WritePartnerSettings` to the personal access token client's allowed scopes, for the `partner` commands outside `partner customer`.
- Organisation update, user removal and invites need organisation membership and no token scope (portal `OrganisationController.cs:49,91,132`), so a token limited to read scopes can still do them.
- Search keys for when a system was last seen and when a pending system enrolled. With them, `--not-seen-for` and `--waiting-for` become one call each.

## Changes to AGENTS.md

If this proposal is accepted, AGENTS.md changes to match:

- Command shape: `<noun> <verb>`, hyphenated verbs for a noun's parts, singular nouns, plural accepted as a hidden alias. The noun list becomes `org`, `partner`, `partner customer`, `system`, `key`, `policy`, `tag`, `dns`, `trust`, `log`.
- `--from-file` and `--template` are removed: every field has a flag.
- Token sources: `ENCLAVE_TOKEN`, then `credentials.json`. `--token` is removed.
- Organisation and partner precedence, including `ENCLAVE_ORG`, `ENCLAVE_ORG_ID` and `ENCLAVE_PARTNER_ID`.
- IDs only after `--id` or an `--<item>-id` option; `--<item>` options take names.
- `--yes` and its exit code are removed: changes run when given, and `--dry-run` previews them.
- Exit 6 for transient failures, and the fixed `code` set.
- The truncation warning on stderr.
- The bulk output shape, empty stdin, the 200-ID limit and the ID checks.
- `commands [<noun> [<verb>]]` replaces `commands --json`.

## Build order

1. `login`, `status`, `org list`, `org use`: the token, context, output, error and exit-code handling every later command uses.
2. `list` and `show` for every top-level noun.
3. Changes, with `--dry-run`: `system`, `key`, `policy`, `tag`.
4. `dns`, `trust`, `org update`, `org list-users`, `org remove-user`, the invite commands, and the commands that wait on `Enclave.Sdk.Api` changes.
5. `partner customer`, once `Enclave.Sdk.Api` has partner clients; the rest of `partner` once the portal change is made.

## Examples

```sh
# 1. Log in from a saved token and work in the Acme organisation
enclave-cli login --token-stdin < ~/.secrets/enclave-token
enclave-cli org use Acme
enclave-cli org use --id 3f2a9c1e-7b4d-4e8a-9c61-2d5b8e0f4a17

# 2. In CI, list the connected Linux build servers in one organisation without touching the shared default
ENCLAVE_TOKEN="$CI_ENCLAVE_TOKEN" ENCLAVE_ORG=Acme enclave-cli system list --tag build --os linux --state connected --all -o id
ENCLAVE_TOKEN="$CI_ENCLAVE_TOKEN" ENCLAVE_ORG_ID=3f2a9c1e-7b4d-4e8a-9c61-2d5b8e0f4a17 enclave-cli system list --tag build --os linux --state connected --all -o id

# 3. Approve every system waiting that enrolled with key 12
enclave-cli system list --pending --key-id 12 --all -o id | enclave-cli system approve -

# 4. Show a reviewer what that approval would send, without sending it
enclave-cli system list --pending --key-id 12 --all -o id | enclave-cli system approve - --dry-run

# 5. Decline systems that have been waiting for approval for more than a week
enclave-cli system list --pending --waiting-for 7d --all -o id | enclave-cli system decline -

# 6. Give contractors access for 8 hours through their policy
enclave-cli policy enable contractors --until 8h --expiry-action disable
enclave-cli policy enable --id 23 --until 8h --expiry-action disable

# 7. Let a visitor's laptop in for a day, then revoke it automatically
enclave-cli system enable LAPTOP7 --until 24h --expiry-action delete

# 8. Make a one-use key for a CI runner, usable only from the CI network, that removes its system 30 minutes after the runner disconnects, and keep its secret
ENROLMENT_KEY=$(enclave-cli key create --description "ci runner 4711" --type ephemeral --approval-mode automatic --uses-remaining 1 --tags ci,runner --allow-ip 198.51.100.0/24 --keep-disconnected 30m -o secret)

# 9. Rotate an enrolment key: create the replacement, then disable the old key
enclave-cli key create --description "build agents 2026-10" --approval-mode manual --tags build,linux && enclave-cli key disable "build agents 2026-04"
enclave-cli key create --description "build agents 2026-10" --approval-mode manual --tags build,linux && enclave-cli key disable --id 12

# 10. Add a tag to a system and keep the tags it has
enclave-cli system update ABCDE --add-tags monitoring

# 11. Let the API servers use the database policy too, checking the request first
enclave-cli policy update "web to db" --set-senders web,api --dry-run
enclave-cli policy update "web to db" --set-senders web,api
enclave-cli policy update --id 42 --set-senders web,api

# 12. Retry a change when the API is briefly unavailable (exit 6), and stop on anything else
for attempt in 1 2 3; do
  enclave-cli system disable ABCDE && break
  [ $? -eq 6 ] || exit 1
  sleep 10
done

# 13. Delete every disabled enrolment key
enclave-cli key list --state disabled --all -o id | enclave-cli key delete --id -

# 14. Revoke systems that have not connected for 90 days
enclave-cli system list --not-seen-for 90d --all -o id | enclave-cli system revoke -

# 15. Point db.internal at the two database systems
enclave-cli dns create-hostname db.internal --systems ABCDE,FGHIJ --notes "primary database pair"

# 16. Remove every hostname for old-api, after checking which ones match
enclave-cli dns list-hostnames --search old-api --all -o table
enclave-cli dns list-hostnames --search old-api --all -o id | enclave-cli dns delete-hostname --id -

# 17. See every option system list takes, and the values each accepts
enclave-cli commands system list

# 18. Make a change in the Acme organisation, whatever the saved default is
enclave-cli policy disable "web to db" --org Acme
enclave-cli policy disable --id 42 --org-id 3f2a9c1e-7b4d-4e8a-9c61-2d5b8e0f4a17

# 19. Offboard a person: remove their account and cancel any invite still pending
enclave-cli org remove-user sam@example.com
enclave-cli org cancel-invite sam@example.com

# 20. Count the systems in every customer organisation
enclave-cli partner customer list --all -o id | while read -r customer; do
  echo "$customer $(enclave-cli system list --org-id "$customer" -o count)"
done

# 21. Let web servers reach the databases on PostgreSQL, checking the request first
enclave-cli policy create --description "web to db" --senders web --receivers db --acl tcp:5432 --dry-run
enclave-cli policy create --description "web to db" --senders web --receivers db --acl tcp:5432

# 22. Give support SSH access to production until 18:00 UTC, then disable the policy
enclave-cli policy create --description "support ssh" --senders support --receivers prod --acl tcp:22 --until 2026-10-06T18:00:00Z --expiry-action disable

# 23. Open DNS and HTTPS from the office laptops to the build farm, only for staff signed in to Entra ID
enclave-cli policy create --description "office to build farm" --senders office,laptop --receivers build --acl udp:53 --acl tcp:443 --trust "entra staff"
enclave-cli policy create --description "office to build farm" --senders office,laptop --receivers build --acl udp:53 --acl tcp:443 --trust-id 3

# 24. Replace a policy's rules: build servers may now reach the artifact store on 443 and 8000-8100 only
enclave-cli policy update "build to artifacts" --set-acl tcp:443 --set-acl tcp:8000-8100
enclave-cli policy update --id 57 --set-acl tcp:443 --set-acl tcp:8000-8100

# 25. Make GW001 a gateway for the office LAN, then route staff traffic for it through GW001
enclave-cli system update GW001 --enable-gateway-for 10.0.0.0/16
enclave-cli policy create --description "staff to office LAN" --senders staff --gateway GW001:10.0.0.0/16 --allow-ip 10.0.0.0/16

# 26. Require staff to be signed in to Entra ID and in the engineering group
enclave-cli trust create --description "entra staff" --type user-auth --authority azure --tenant 9b1c3a52-7f0e-4d8a-b0a4-2c6e1d5f8a31 --claim groups=4e2d8c1a-0b7f-4c39-9a65-1f3e7d2b6c84

# 27. Refuse connections from outside the UK and from one known bad range
enclave-cli trust create --description "uk only" --type public-ip --allow-country GB --block-ip 203.0.113.0/24

# 28. Every system tagged prod must also meet the "uk only" requirement
enclave-cli tag set prod --trust "uk only"
enclave-cli tag set prod --trust-id 5

# 29. Give each system tagged web a name in the internal zone automatically
enclave-cli dns create-zone internal --auto-dns-tags web

# 30. Allow the cleaning crew's tablets in only during office hours
enclave-cli policy create --description "facilities tablets" --senders tablets --receivers printers --acl tcp:9100 --active-hours "mon-fri 08:00-18:00 Europe/London"

# 31. Disable a policy by its description
enclave-cli policy disable "web to db"
enclave-cli policy disable --id 42

# 32. Stop a key enrolling new systems without approval
enclave-cli key update "build agents" --approval-mode manual
enclave-cli key update --id 12 --approval-mode manual

# 33. Show the last day's activity
enclave-cli log --since 24h -o table

# 34. Disable a policy by its ID
enclave-cli policy disable --id 42

# 35. Disable several policies by ID in one call
enclave-cli policy disable --id 42,43,57

# 36. Check which token, organisation and partner the CLI will use
enclave-cli status

# 37. Find every gateway and the subnets it serves
enclave-cli system list --gateway -o table

# 38. Show the ten systems that connected most recently
enclave-cli system list --sort recently-connected --limit 10 -o table

# 39. Find the system that answers to db.internal
enclave-cli system list --dns-name db.internal

# 40. Count the Windows systems that are offline
enclave-cli system list --os windows --state disconnected -o count

# 41. Tag a system before approving it, then approve it
enclave-cli system update XYZ12 --pending --set-tags kiosk,lobby && enclave-cli system approve XYZ12

# 42. Take a tag off a system
enclave-cli system update ABCDE --remove-tags legacy

# 43. Stop GW001 acting as a gateway
enclave-cli system update GW001 --disable-gateway

# 44. Disable three systems at once
enclave-cli system disable ABCDE FGHIJ KLMNO

# 45. Disable every system listed in a file, one ID per line
enclave-cli system disable - < quarantine.txt

# 46. Print the secret of an existing enrolment key
enclave-cli key show "build agents" -o secret
enclave-cli key show --id 12 -o secret

# 47. List the keys that approve automatically and are tagged ci
enclave-cli key list --approval automatic --tag ci -o table

# 48. Give a key five more uses and restrict it to the office network
enclave-cli key update "build agents" --uses-remaining 5 --set-allow-ip 203.0.113.0/24
enclave-cli key update --id 12 --uses-remaining 5 --set-allow-ip 203.0.113.0/24

# 49. Let a key enrol for two weeks, then delete it
enclave-cli key enable "contractor laptops" --until 14d --expiry-action delete
enclave-cli key enable --id 31 --until 14d --expiry-action delete

# 50. List the disabled policies
enclave-cli policy list --state disabled -o table

# 51. Remove a policy's office-hours restriction
enclave-cli policy update "facilities tablets" --set-active-hours ""
enclave-cli policy update --id 61 --set-active-hours ""

# 52. Allow ping and a UDP port range between the monitoring and app servers
enclave-cli policy create --description "monitoring" --senders monitoring --receivers app --acl icmp --acl udp:8125-8126

# 53. Route the office LAN through two gateways, so either can carry it
enclave-cli policy create --description "office LAN" --senders staff --gateway GW001:10.0.0.0/16 --gateway GW002:10.0.0.0/16

# 54. See what deleting a policy would send, then delete it
enclave-cli policy delete "old vpn" --dry-run
enclave-cli policy delete "old vpn"
enclave-cli policy delete --id 17 --dry-run
enclave-cli policy delete --id 17

# 55. Rename a tag and give it a colour
enclave-cli tag set web --name frontend --colour "#2f80ed"

# 56. Require users to be signed in to the Enclave portal
enclave-cli trust create --description "portal login" --type user-auth --authority portal

# 57. Require a sign-in through a generic OIDC provider, from the example.com domain
enclave-cli trust create --description "sso" --type user-auth --authority oidc --authority-uri https://sso.example.com --claim hd=example.com

# 58. Allow Ireland as well as the UK
enclave-cli trust update "uk only" --set-allow-country GB,IE
enclave-cli trust update --id 5 --set-allow-country GB,IE

# 59. List the IP-based trust requirements
enclave-cli trust list --type public-ip -o table

# 60. Name systems tagged web or api in the internal zone automatically
enclave-cli dns update-zone internal --set-auto-dns-tags web,api
enclave-cli dns update-zone --id 4 --set-auto-dns-tags web,api

# 61. Add a third database system to db.internal
enclave-cli dns update-hostname db.internal --set-systems ABCDE,FGHIJ,KLMNO
enclave-cli dns update-hostname --id 7 --set-systems ABCDE,FGHIJ,KLMNO

# 62. List every hostname in the internal zone
enclave-cli dns list-hostnames --zone internal -o table
enclave-cli dns list-hostnames --zone-id 4 -o table

# 63. Rename the organisation and set its contact details
enclave-cli org update --name "Acme Ltd" --website https://acme.example --phone "+44 20 7946 0000"

# 64. Invite a new administrator, then check the pending invites
enclave-cli org invite alex@example.com && enclave-cli org list-invites

# 65. As a partner, make one of your staff an admin of a customer and turn on auto-sync for that customer
enclave-cli partner customer add-admin "Globex Ltd" --user alex@example.com && enclave-cli partner customer enable-auto-sync "Globex Ltd"
enclave-cli partner customer add-admin --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --user-id 5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25 && enclave-cli partner customer enable-auto-sync --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b

# 66. Invite someone at the customer to be its admin
enclave-cli partner customer invite "Globex Ltd" --email sam@globex.example

# 67. Sign out on a shared machine
enclave-cli logout
```
