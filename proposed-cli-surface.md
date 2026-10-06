# Proposed CLI surface

`enclave-cli` drives the Enclave Management API and the Enclave Partner API through `Enclave.Sdk.Api` 1.0.4. AI agents are the main users; people use it too. This file lists the commands and the behaviour they share. Where it changes the CLI contract in AGENTS.md, the change is listed under "Changes to AGENTS.md".

## Shape and naming

`enclave-cli <noun> <verb> [arguments] [options]`

- A command is a noun and a verb: `system list`, `policy create`. A noun's own parts take a hyphenated verb in place of another level: `dns create-hostname`, `org remove-user`, `partner customer add-admin`. `partner customer` is the one second-level noun, since customers have a dozen verbs of their own.
- Nouns are singular. Most commands act on one item (`policy delete <policy>`), and singular reads correctly there. A plural name is accepted as a hidden alias (`systems list` runs `system list`), so a guess works; help and `commands` show the singular name only.
- Nouns use the same verbs wherever the API supports them: `list`, `show`, `create`, `update`, `delete`, `enable`, `disable`. Other verbs take the API's name: `revoke`, `approve`, `decline`, `convert`, `invite`.
- A flag is named for what it does, in plain words: `--auto-approve` sets `ApprovalMode`, `--uses` sets `UsesRemaining`, `--ephemeral` sets `Type`. Where an API field name already reads plainly, the flag keeps it (`--notes` sets `Notes`), shortened where it is long: `--senders` sets `SenderTags`, `--receivers` sets `ReceiverTags`, `--trust` sets `SenderTrustRequirements`.

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
│   ├── use --id <partnerId>
│   └── customer
│       ├── list
│       ├── show <customer>
│       ├── create <name>
│       ├── update <customer>
│       ├── convert <customer>                  converts the customer to paid
│       ├── list-admins <customer>
│       ├── add-admin <customer> --user-id <accountId>
│       ├── remove-admin <customer> --user <email> | --user-id <accountId>
│       ├── list-invites <customer>
│       ├── invite <customer> --email <email>
│       ├── cancel-invite <customer> --email <email>
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
│   ├── create <description>
│   ├── update <key>
│   ├── enable <key...>
│   ├── disable <key...>
│   └── delete <key...>                         needs an Enclave.Sdk.Api change
├── policy
│   ├── list
│   ├── show <policy>
│   ├── create <description>
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
│   ├── create <description>
│   ├── update <trust>
│   └── delete <trust...>
├── log                                         the organisation's activity log
└── commands [<noun> [<verb>]]                  every command, option, value and error code, as JSON
```

- A customer is an organisation. `<customer>` is its name, or `--org-id <orgId>` gives its organisation ID, the only ID a customer has (portal `CustomersController.cs:69` reads the customer ID as an `OrganisationGuid`). Anything else a partner customer command needs is a named option (`--user`, `--user-id`, `--email`).
- An ID is never a positional argument. `--id` gives the ID of the command's own item. Every other item has a pair of options: `--<item>` takes its name (an email address for a user) and `--<item>-id` takes its ID: `--org`/`--org-id`, `--user`/`--user-id`, `--key`/`--key-id`, `--zone`/`--zone-id`, `--trust`/`--trust-id`, and `--partner-id` (partners are given by ID only). The option decides how its value is read, and the CLI never inspects a value to guess its type: `--key 12` is a key described "12", `--key-id 12` is key 12, and `--user-id` takes only a GUID. Nothing in `42` or a GUID says what it identifies, so the option name does. Positional arguments are names, email addresses, hostnames, tag names, and system IDs, since systems have no names.
- `<key>`, `<policy>`, `<trust>`, `<zone>` and `<hostname>` are names: a description, a zone's name, a full hostname. `--id 42` gives the ID instead, and `--id 42,43,57` several.
- `--pending` on `system list`, `show` and `update` acts on systems waiting for approval, which the API keeps apart from enrolled systems (`unapproved-systems`). `approve` and `decline` act on those systems only.
- A hostname is written in full, `db.internal`, and its zone is the zone its name ends in. Zones are named, `internal`; IDs work too.

## Command options

| Command | Options |
|---|---|
| `org update` | `--name`, `--website`, `--phone` |
| `system list` | `--filter <text>`, `--tag a,b`, `--state connected\|disconnected`, `--os windows\|linux\|mac`, `--type general\|ephemeral`, `--gateway`, `--key <name>`, `--key-id <id>`, `--not-seen-for <duration>`, `--include-disabled`, `--sort` |
| `system update` | `--description`, `--notes`, `--set-tags a,b`, `--add-tags a,b`, `--remove-tags a,b`, `--enable-gateway-for <subnet>[=<label>]` (repeatable), `--disable-gateway` |
| `system enable` | `--for <duration>` or `--until <time>`, `--then disable\|revoke` |
| `key enable`, `policy enable` | `--for <duration>` or `--until <time>`, `--then disable\|delete` |
| `system list --pending` | `--filter <text>`, `--tag a,b`, `--key <name>`, `--key-id <id>`, `--waiting-for <duration>`, `--sort` |
| `system update --pending` | `--description`, `--notes`, `--set-tags a,b`, `--add-tags a,b`, `--remove-tags a,b` |
| `key list` | `--filter <text>`, `--tag a,b`, `--approval automatic\|manual`, `--state enabled\|disabled\|no-uses`, `--include-disabled`, `--sort` |
| `key create <description>` | `--ephemeral`, `--auto-approve`, `--uses <n>`, `--tags a,b`, `--allow-ip <range>[=<label>]` (repeatable), `--keep-disconnected <duration>`, `--for <duration>` or `--until <time>`, `--then disable\|delete`, `--notes` |
| `key update` | `--description`, `--notes`, `--auto-approve`, `--require-approval`, `--uses <n>`, `--set-tags a,b`, `--add-tags a,b`, `--remove-tags a,b`, `--set-allow-ip <range>[=<label>]` (repeatable), `--keep-disconnected <duration>` |
| `policy list` | `--filter <text>`, `--tag a,b`, `--state enabled\|disabled`, `--include-disabled`, `--sort` |
| `policy create <description>` | `--senders a,b`, `--receivers a,b`, `--acl <protocol>[:<ports>][=<label>]` (repeatable), `--trust <name>,...`, `--trust-id <id>,...`, `--gateway <systemId>:<route>,...` (repeatable), `--mode balanced\|ordered\|geographic`, `--subnet-filter <range>[=<label>]` (repeatable), `--active-hours <hours>`, `--for <duration>` or `--until <time>`, `--then disable\|delete`, `--notes`, `--disabled` |
| `policy update` | `--description`, `--notes`, `--set-senders a,b`, `--set-receivers a,b`, `--set-acl <protocol>[:<ports>][=<label>]` (repeatable), `--set-trust <name>,...`, `--set-trust-id <id>,...`, `--set-gateway <systemId>:<route>,...` (repeatable), `--mode balanced\|ordered\|geographic`, `--set-subnet-filter <range>[=<label>]` (repeatable), `--set-active-hours <hours>` |
| `trust list` | `--filter <text>`, `--type user-auth\|public-ip`, `--sort` |
| `trust create <description>` | `--notes`; a sign-in requirement: `--authority portal\|azure\|google\|okta\|jumpcloud\|duo\|oidc`, `--tenant <id>` (azure), `--authority-uri <url>` (oidc), `--claim <claim>=<value>` (repeatable); a public IP requirement: `--allow-ip <range>[=<label>]`, `--block-ip <range>[=<label>]`, `--allow-country <code>[=<label>]`, `--block-country <code>[=<label>]` (each repeatable) |
| `trust update` | `--description`, `--notes`, and the create flags for its type with a `--set-` prefix (`--set-claim`, `--set-allow-ip`, ...). Each replaces only the conditions of its own kind: `--set-allow-country` replaces the allowed countries and leaves the IP ranges and blocked countries as they are |
| `tag list` | `--filter <text>`, `--sort` |
| `tag set` | `--name <new>`, `--colour`, `--trust <name>,...`, `--trust-id <id>,...`, `--notes` |
| `dns create-zone` | `--auto-dns-tags a,b`, `--notes` |
| `dns update-zone` | `--name`, `--set-auto-dns-tags a,b`, `--notes` |
| `dns list-hostnames` | `--zone <name>`, `--zone-id <id>`, `--filter <text>` |
| `dns create-hostname` | `--tags a,b`, `--systems id,id`, `--notes` |
| `dns update-hostname` | `--name`, `--set-tags a,b`, `--add-tags a,b`, `--remove-tags a,b`, `--set-systems id,id`, `--notes` |
| `partner customer create <name>` | `--owner <email>`, `--domain <domain>`, `--contact <name>`, `--systems <n>`, `--gateways <n>`, `--industry-discount`, `--hard-limit`, `--auto-sync` |
| `partner customer update` | `--name`, `--contact <name>`, `--systems <n>`, `--gateways <n>`, `--industry-discount` or `--no-industry-discount`, `--hard-limit` or `--no-hard-limit` |
| `partner customer convert` | `--billing-months 1\|12\|24\|36` (required) |
| `log` | `--limit <n>`, `--since <when>`, `--until <when>`, `--user <email>`, `--level information\|warning\|error`, `--filter <text>` |

- `--acl` takes a protocol (`any`, `tcp`, `udp`, `icmp`) and, for TCP and UDP, a port or range: `tcp:5432`, `udp:53`, `tcp:8000-8100`. `policy create` requires at least one `--acl`, and `--acl any` allows every protocol, so the command always states what traffic the policy allows. The API accepts a policy with no ACLs, and the agent then allows no traffic through it (fabric `StateTracker.cs:909-921`; the API reports such a policy as `InactiveNoAcls`, portal `PolicyModelExtensions.cs:25-29`).
- `--gateway GW001:10.0.0.0/16,10.1.0.0/16` makes a gateway policy: the senders reach those routes through system GW001. `--subnet-filter` narrows the addresses they may reach through it; it is the portal's "Subnet filter" (portal-spa `policies.json:69`) and the API's `gatewayAllowedIpRanges`. It applies to gateway policies only: without `--gateway` it exits 2, since the API accepts allowed ranges on gateway policies only (portal `PolicyCreateModelValidator.cs:47`). The API sets no limit on how many there are. `--gateway` and `--receivers` together exit 2. The CLI sends the traffic direction `exit`, the only one the API accepts (portal `PolicyCreateModelValidator.cs:56` rejects `entry`).
- `--mode` decides which of several gateways carries a system's traffic. The values are the API's `GatewayPriority` values (sdk `Enclave.Sdk.Network/NetworkPolicy/GatewayPriorityType.cs`, which the API uses; portal-spa `types/api.ts:3242`):

  | Value | Gateway used |
  |---|---|
  | `balanced` | whichever connects first, which spreads systems across the gateways |
  | `ordered` | the first in the order the `--gateway` flags are given that is online; the others are standbys |
  | `geographic` | the one closest to the system |

  Without the flag the CLI sends `balanced`. `--mode` applies to gateway policies only: on `policy create` without `--gateway` it exits 2.
- `--enable-gateway-for 10.0.0.0/16 --enable-gateway-for 10.1.0.0/16` makes the system a gateway for those subnets, replacing any it had; `--disable-gateway` stops it acting as one.
- `--subnet-filter`, `--allow-ip`, `--block-ip`, `--allow-country`, `--block-country`, `--acl` and `--enable-gateway-for` take an optional label after `=`, which the API stores with each entry: `--subnet-filter "104.47.0.0/17=Exchange Online"`. The label is sent as the entry's `description` (`gatewayAllowedIpRanges`, `ipConstraints`, `acls`, trust requirement `conditions`) or `name` (`gatewayRoutes`). The CLI splits at the first `=`, which no range, country code, ACL or subnet contains, so a label can itself contain `=`. These flags are repeated, never comma lists, since a label can contain a comma.
- A flag that replaces a list keeps the label of every entry that stays: `--set-subnet-filter 104.47.0.0/17` on a policy that already has that range labelled "Exchange Online" keeps the label. A label given with the flag replaces it, and an empty one, `104.47.0.0/17=`, removes it. New entries without a label have none. Labels set in the portal survive an update from the CLI.
- `--active-hours "mon-fri 08:00-18:00 Europe/London"` takes days, a start and end time, and an IANA time zone (UTC when left out). `--set-active-hours ""` removes the restriction.
- `--keep-disconnected 30m` keeps systems enrolled with an ephemeral key for that long after they disconnect. Without `--ephemeral` it exits 2; the API accepts it only on ephemeral keys (portal `EnrolmentKeyCreateValidator.cs:24`).
- `--for 8h` (a duration: `30m`, `8h`, `14d`) or `--until <time>` makes the change temporary. Afterwards the item is disabled; `--then revoke` (systems) or `--then delete` (keys, policies) removes it instead. `enable` and `create` both take them.
- `--until` takes an RFC 3339 time with its zone (`2026-10-09T17:30:00Z`, `2026-10-09T17:30:00-04:00`), or a time without a zone, which is read in the machine's time zone: a date and time, `2026-10-09T17:30`, or a clock time, `18:00`, meaning its next occurrence. The CLI takes the time zone from the system, and the user never sets it. Formats do not follow the system locale, so a command means the same on every machine. `--since` on `log` reads times the same way. Output times are UTC.
- `key create` sends every setting of the key, with the portal's values (portal-spa `createKeyDetailsSaga.ts:21-29`, `Purpose/Create.tsx:54-59`):

  | Key | Approval | Uses | Keeps disconnected systems |
  |---|---|---|---|
  | general-purpose | required unless `--auto-approve` | unlimited unless `--uses` | not applicable |
  | `--ephemeral` | automatic | unlimited | 30 minutes unless `--keep-disconnected` |

  As in the portal, an ephemeral key always approves automatically and has unlimited uses, so `--ephemeral` with `--auto-approve` or `--uses` exits 2.
- `dns create-hostname` sends the record type `ENCLAVE`, the only type the API has (portal `DnsRecordTypeFormatConverter.cs:12`).
- `trust create` takes its type from its flags: `--authority` makes a sign-in requirement, `--allow-*` and `--block-*` a public IP requirement, and mixing the two exits 2.
- A public IP requirement is a list of conditions, each an IP range or a country, allowed or blocked (`{type, value, isBlocked, description}`, portal `TrustRequirementSettingsPublicIpValidator.cs:27`). The CLI sends `isBlocked` on every condition; the check skips an IP condition without it (services `Enclave.Discover/TrustValidators/PublicIpValidator.cs:163-167`). A system's public address must pass both checks (same file, 31-144):

  | Check | Passes when |
  |---|---|
  | ranges | no `--allow-ip` is given and no `--block-ip` range contains the address, or an `--allow-ip` range contains it. Where an allowed and a blocked range both contain it, the smaller range wins |
  | countries | the address's country is not blocked, and is allowed when any `--allow-country` is given |

  The ranges are checked first, and a failed range check is not overridden by an allowed country.
- `--claim groups=<object id>` requires that claim in the user's sign-in token. Countries are ISO 3166 two-letter codes.
- `tag set <tag>` updates the tag, and creates it when it does not exist: the API has separate create and update calls, and to a user both mean "make tag `web` look like this". `--trust` replaces the tag's trust requirements. `--name` renames, so the tag must exist.
- A tag named on a system, key or policy appears in `tag list` without being created, and the API deletes it again when nothing uses it. `tag set` makes a tag permanent: it stays when nothing uses it (portal `TagsRepository.cs:356-370, 432-437`).
- `partner customer create` sends every field of the API's `CustomerCreateModel`, with the partner portal's values when a flag is left out (portal `Enclave.Partner.Portal.Client/Pages/AddNewCustomer.razor:315-345`, `Shared/AdminTable.razor:171`):

  | Flag | API field | Without the flag |
  |---|---|---|
  | `<name>` | `Name` | required |
  | `--owner <email>` | `OwnerEmail`, the user who will own the customer's organisation | not sent |
  | `--domain <domain>` | `Domain` | not sent |
  | `--contact <name>` | `ContactName` | not sent |
  | `--systems <n>` | `InitialSystemsCount`, at least 2 | 50 |
  | `--gateways <n>` | `InitialGatewaysCount` | 0 |
  | `--industry-discount` | `IndustryDiscount`, a request for the discount | off |
  | `--hard-limit` | `HardLimit` | off |
  | `--auto-sync` | `AdminAutoSyncIsEnabled` | off |

- `partner customer update` sets `LicensedAgentsCount` with `--systems` (at least 2) and `LicensedGatewaysCount` with `--gateways` (portal `CustomerPatchModelValidator.cs:14-16`). Auto-sync changes through `enable-auto-sync` and `disable-auto-sync`, which have their own API routes.
- `partner customer convert` requires `--billing-months`, one of the periods the API accepts (portal `ConvertCustomerModelValidator.cs:13-17`), so the command states the billing period it commits to; the API's default is 1.
- `log` prints the newest 100 entries, newest first; `--limit` changes the number. `--since 24h` (a duration, or a time as for `--until`) prints every entry back to then, with no limit. The API returns entries newest first (portal `ActivityLogRepository.cs:43`), so the CLI stops reading at the first entry older than `--since`. `--until` leaves out entries newer than that time.
- The logs API takes only a page and page size (portal `LogsRequestModel.cs`), so the CLI applies `--user`, `--level` and `--filter` to the entries it reads. `--user` matches the entry's `userName`, `--level` takes one or more levels (`--level warning,error`), and `--filter` matches text in the message. With these filters and no `--since`, `--limit` counts matching entries, and the CLI reads back until it has that many or reaches the start of the log.

## Options on every command

| Option | Applies to | Meaning |
|---|---|---|
| `--org <name>`, `--org-id <orgId>` | commands that act within an organisation | the organisation to use (see "Context") |
| `--partner-id <partnerId>` | `partner` commands | the partner to use (see "Context") |
| `--verbose` | every command | diagnostics on stderr |
| `--dry-run` | commands that change something through the API | prints the request and sends nothing |

Option values that name an API enum (`--sort`, `--type`, `--approval`, `--then`, `--state`, `--os`, `--level`, `--mode`) take lower-case, hyphenated names (`recently-connected`, `general`, `automatic`, `revoke`), matched ignoring case. A value is renamed where the API's name does not say what it does: `--then revoke` for `Delete` on a system. JSON output keeps the API's own names. `commands` lists the allowed values.

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

- A command that takes several items takes any number of them: system IDs or names as arguments, or IDs as `--id 42,43,57`. `-` in place of the arguments reads a list printed by an `enclave-cli` list command from stdin and acts on its items by ID; duplicates are removed.
- The list's `kind` must match the command: `system approve` and `decline` take the output of `system list --pending`, the other `system` verbs take `system list`, `key` verbs take `key list`, and so on. Any other kind, or input that is not a list, exits 2. Keys, policies, zones, hostnames and trust requirements all have integer IDs, so the kind is what stops `key list | enclave-cli policy delete -` deleting the policies that share those numbers.
- A command that accepts several IDs always makes the bulk call, also for one ID, so its output is always `{ "requested": n, "affected": m }`. The bulk calls return a count only, and bulk approve succeeds when some IDs were not approved (portal `UnapprovedSystemsController.cs:170-177`). `affected` lower than `requested` means some IDs were unknown or already in that state; the exit code is 0, so a re-run after a timeout is safe.
- The API takes at most 200 IDs per bulk call (portal `Enclave.Utilities/HardLimits.cs:22`, `MaxBulkIds`), so the CLI sends more than 200 in calls of 200 and adds up `requested` and `affected`. If a call fails, the CLI stops, and the error carries `requested` and `affected` for the calls that succeeded. Running the command again is safe for the reason above.
- `-` given an empty list prints `{ "requested": 0, "affected": 0 }`, makes no call and exits 0, so a pipeline fed by an empty list succeeds.
- `-` when stdin is a terminal exits 2. The CLI never waits for input.
- `--for` and `--until` take one item and print the updated model; the API's timed enable has no bulk form. `dns delete-zone` takes one zone; the API has no bulk zone delete (portal `DnsController.cs:145`).
- Single-ID commands (`show`, `update`, `--for` and `--until`, `dns delete-zone`, and the partner and customer commands) print the model and exit 5 for an unknown ID.

Example, approving every system enrolled with key 12:

```
enclave-cli system list --pending --key-id 12 | enclave-cli system approve -
```

## Filters

`list` commands return every matching item: the CLI reads every page. `log` is the exception (see "Command options"). `--filter <text>` works as the portal's search box does: the text is sent as typed, so it takes the API's search syntax as well as plain words (`--filter "web tags:|prod,staging version:>2024.8.0"`), and plain words match the list's main field, such as a system's name, ID or hostname (portal-spa `ApiService.ts:123-141`; portal `BaseSearchKeyService.cs`). Lists also take one flag per search key the API defines for that resource (portal `Enclave.Configuration.Data/Modules/*/*SearchKeyService.cs`): for `system list`, `--tag`, `--state`, `--os`, `--type`, `--gateway` and `--key`. The CLI adds them to the `--filter` text (`tags:web state:connected`), and they combine: `system list --tag web --state connected` lists connected systems tagged `web`. `--tag web,db` lists items with every tag given. On `policy list`, `--tag` matches sender or receiver tags (portal `PolicySearchKeyService.cs`). The API's `key:` search matches part of a key's name, so for `--key` and `--key-id` the CLI also keeps only systems whose `enrolmentKeyId` is that key's. `--state disabled` lists disabled items without `--include-disabled`.

`system list --not-seen-for 90d` and `system list --pending --waiting-for 7d` have no API search key, so the CLI keeps the matches from the items it reads; `total` counts the matches.

## Names and IDs

Keys, policies, DNS zones, hostnames and trust requirements are given as arguments by name: the description of a key, policy or trust requirement, the name of a zone, or the full name of a hostname (`db.internal`). Names are looked up with one list call, and each must match exactly one item, ignoring case; no match, or several, exits 2 with the candidates. `--id` gives IDs instead and makes no lookup, and so does a list read from stdin, which carries each item's ID. Options that point at another item come in pairs, one for its name and one for its ID (`--key`/`--key-id`). Systems take IDs only: hostnames are not unique. Tags are given by name only. `org remove-user` takes an email address in place of the account ID, looked up the same way. A partner customer is given by its name, looked up in the partner's customer list, or by `--org-id`. `partner customer remove-admin` takes the admin's email address (`--user`), looked up in the customer's admins, or `--user-id`. `partner customer cancel-invite` takes the invited email address, looked up in the customer's invites. `partner customer add-admin` takes `--user-id` only: the account is a partner user, and listing partner users needs a scope personal access tokens cannot carry (see "Partner API"). The partner portal shows the account ID.

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

No command asks for confirmation; `--dry-run` shows the request before it is sent. The trade-off is that a mistaken `revoke` or `delete` runs, so an agent's token should carry only the scopes it needs (`RequestedScopes` when the token is created). Approving a waiting system needs only the `CanWriteSystems` policy, the same as editing its description (portal `UnapprovedSystemsController.cs:128,158`), so a token that can edit systems can also admit them. Organisation update, user removal and invites need organisation membership and no token scope (portal `OrganisationController.cs:49,91,132`), so a token limited to read scopes can still do them.

## Dry run

`--dry-run` prints the request `Enclave.Sdk.Api` builds, with the organisation or partner, and sends nothing. The Authorization header is left out.

```
{ "dryRun": true, "org": { "id": "…", "name": "…" }, "request": { "method": "PUT", "url": "https://api.enclave.io/org/…/systems/disable", "body": { … } } }
```

Capturing the request needs an `Enclave.Sdk.Api` change (see below). Printing the captured request keeps URL building in one place and shows exactly what would be sent.

## Create and update

- Every field is set with a flag. An update sends only the fields given; the rest are left as they are.
- A create sends a value for every setting that changes behaviour. When a flag is left out, it sends the value this document gives, for example enabled unless `--disabled`, so a change to the API's own defaults does not change what a command does. `--dry-run` shows every value sent.
- `--set-tags` and `--set-systems` replace the whole list, and `--set-tags ""` clears it. Tags decide policy membership, so the flag's name says it replaces.
- `--add-tags` and `--remove-tags` change the list in place: the CLI reads the item, then patches the whole list. A change someone else makes between the two calls is overwritten; `--set-tags` gives an exact list.

## Output

- JSON, using the `Enclave.Sdk.Api` models unchanged. There is one output format. Agents read JSON, and a format for people alone is not worth a flag on every command.
- Lists: `{ "kind": "system", "items": [...], "total": <n> }`. `kind` names what the items are (`system`, `pending-system`, `key`, `policy`, `tag`, `zone`, `hostname`, `trust`, `log`, and the `org` and `partner` lists), so a command reading the list from stdin can check it.
- A list is printed once every page has been read. If a page read fails, the command exits with that error and prints nothing, so stdout never holds part of a list.
- Enrolment key output includes the key's secret, `key` (`EnrolmentKeyModel.Key`).
- `commands system list` describes one command, its options and their allowed values.
- Common questions have a filter, so a script needs `jq` only to pull out a single field, such as a new key's secret.

## Errors and exit codes

stderr carries one JSON object per error: `{ "error": { "code", "status", "title", "detail", "errors" } }`. `code` comes from a fixed set the CLI owns, listed by `commands`. `status`, `title`, `detail` and `errors` carry the API's problem details when the API sent them.

| Exit | `code` | Meaning |
|---|---|---|
| 0 | | success |
| 1 | `api_error`, `not_implemented` | any other API error; a command the CLI lists but cannot run, which is every `partner` command until `Enclave.Sdk.Api` has partner clients. It makes no call |
| 2 | `invalid_argument`, `no_org`, `no_partner` | bad arguments, bad ID, no organisation or partner chosen |
| 3 | `token_missing`, `token_invalid` | no token, or HTTP 401 |
| 4 | `forbidden` | HTTP 403: the token lacks the scope |
| 5 | `not_found` | HTTP 404 on a single-ID command |
| 6 | `transient` | network failure, timeout, HTTP 429 or 5xx: retrying can succeed |

Parse errors (unknown option, missing argument) follow the same rules: JSON on stderr, nothing on stdout, exit 2, with any suggestion in `detail`. System.CommandLine's default writes plain text and help and exits 1, so the CLI replaces its parse-error action.

`Enclave.Sdk.Api` throws `EnclaveApiException` only for `application/problem+json` responses (`Handlers/ProblemDetailsHttpMessageHandler.cs:20`). The CLI maps `HttpRequestException` status codes to the same exit codes, so a plain 401 or a proxy's 502 still gets 3 or 6.

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

- A list makes one call per page of 200, the most the API returns per page (portal `PaginationDefaults.cs:11`), until the response's `metadata.nextPage` is null. `log` reads only the pages it needs.
- A bulk command makes one call per 200 IDs.
- Looking up an organisation by name, or with no organisation chosen, adds one call.
- A key, policy, zone or trust requirement given by name adds one call, and so does a customer given by name, or an admin or invite given by email.
- `--add-tags` and `--remove-tags` read the item first, which adds one call. So do `--set-subnet-filter`, `--set-allow-ip`, `--set-acl`, `--enable-gateway-for` and the `trust update` `--set-` flags, to keep existing labels and, on trust requirements, the conditions of other kinds.
- `tag set` on a tag that does not exist makes a second call to create it.

## Partner API

The partner API is a separate service (portal `src/Enclave.Partner.Api`) with every route under `/partner/{partnerId}/`. It covers the partner (properties, users, invites) and its customers (properties, admins, invites, auto-sync). The CLI uses the customer routes only, the ones a personal access token can reach.

- It runs on its own host: `PartnerApiUrl`, `http://partner-api.local:8083` in development (portal `Enclave.Partner.Portal.Server/appsettings.Development.json:5`) and set at deployment in production. `Enclave.Sdk.Api` needs partner clients and a partner base URL; `credentials.json` holds one `baseUrl`.
- Personal access tokens can carry the partner scopes `ReadCustomers` and `WriteCustomers`, and no other partner scope (portal `Enclave.Accounts/Config/EnclaveIdentityClientConfig.cs:195-216`). The partner API requires the scope claim on the token (portal `Enclave.Api.Scaffolding/Authorisation/AuthorizationExtensions.cs:38`). With a personal access token:
  - The customer routes work: reading needs `ReadCustomers`, and changes and the invite list need `WriteCustomers` (portal `CustomersController.cs`).
  - The partner's own properties, users and invites need `ReadPartnerList`, `ReadPartnerInfo` or `WritePartnerSettings` (portal `Enclave.Partner.Api/WebStartup.cs:115-121`), which a personal access token cannot carry, so the CLI has no commands for them.
  - The partner ID cannot be looked up, so `partner use` and `--partner-id` take an ID. The partner portal shows it.
- A customer is an organisation, and the partner API identifies it by its organisation ID (portal `CustomersController.cs:69` parses the route's customer ID as an `OrganisationGuid`). Partner staff work on a customer's systems, policies and the rest through the main API, where `org list` includes the customer's organisation with `PartnerAccess` set:

  ```
  enclave-cli partner customer list
  enclave-cli system list --org-id <orgId>
  ```

- `org cancel-invite` and `partner customer cancel-invite` both take an email address. The main API cancels an invite by email (`CancelInviteAync(email)`); the partner API cancels by invite ID, so the CLI looks the email up in the customer's invites (`CustomerAdminInviteModel` carries both).

## Left out

- Enrolling a system (`IAuthorityClient.EnrolAsync`): the agent `enclave` does that.
- Account settings (password, 2FA) and accepting organisation or partner invites: these are browser sign-in flows.
- Payment, and the partner API's Gradient billing endpoints.
- The partner API's `countries` and `referlink` (data for the partner portal's screens) and `customer oldest-version`.
- The partner's own properties, users and invites, and listing partners: personal access tokens cannot carry the scopes they need (see "Partner API").

## Needs `Enclave.Sdk.Api` changes

Each is added in enclave-networks/enclave.sdk.api first, with tests there, then used here. The API itself does not change: everything here uses routes and fields the API has. `Enclave.Sdk.Api.Data` is built from the portal repository, so a model it lacks or has wrong is defined or corrected in `Enclave.Sdk.Api`.

1. Escape IDs in URL paths.
2. An HTTP handler option on `EnclaveClientOptions`, for `--dry-run`.
3. Allow `null` in a patch, so an update flag given an empty value (`--set-active-hours ""`) can clear a field; `PatchClient.Set` rejects null (`Data/PatchClient.cs:32`).
4. Enrolment key delete, single and bulk, for `key delete`. The API has both (portal `EnrolmentKeysController.cs:265,295`).
5. `CreateOrganisationClient(OrganisationGuid)`. `CreateOrganisationClient` takes an `AccountOrganisationModel` and uses only its `OrgId` (`OrganisationClient.cs:25`), so building one from a saved ID means filling the role and partner-access fields with placeholders.
6. Status checks on calls that pass a failure through as success when the response is not problem+json: `RemoveUserAsync`, `InviteUserAsync`, `CancelInviteAync` (`OrganisationClient.cs:91-133`) and the single create, enable and disable calls.
7. The `hostname` filter on DNS record list. The API accepts it (portal `DnsRecordsRequestModel.cs:18`).
8. The `meta/search-keys` endpoints, so `commands` can describe the keys `--filter` accepts.
9. Partner API clients for the customer routes (customers, customer admins, customer invites, auto-sync), and a partner API base URL.
10. `GatewayPriorityType` in `Enclave.Sdk.Api.Data` 304.48.0, the version `Enclave.Sdk.Api` 1.0.4 uses, has `Prioritised` where the API has `Ordered`. The package compiles its own copy of the enum (portal `Enclave.Sdk.Api.Data/Duplicated/GatewayPriorityType.cs`), and the API uses the SDK's (sdk `Enclave.Sdk.Network/NetworkPolicy/GatewayPriorityType.cs`). `Enclave.Sdk.Api` writes and reads enums by name (`Constants.cs:17`, `JsonStringEnumConverter`), so it would send `Prioritised`, which the API does not accept, and reading a policy set to `Ordered` would fail. `Enclave.Sdk.Api` maps the value itself, writing and reading `Ordered`.

## Changes to AGENTS.md

If this proposal is accepted, AGENTS.md changes to match:

- Command shape: `<noun> <verb>`, hyphenated verbs for a noun's parts, singular nouns, plural accepted as a hidden alias. The noun list becomes `org`, `partner`, `partner customer`, `system`, `key`, `policy`, `tag`, `dns`, `trust`, `log`.
- `--from-file` and `--template` are removed: every field has a flag.
- Temporary changes: `--for <duration>` or `--until <time>` (RFC 3339, or local time without a zone), then `--then`.
- `-o` is removed: output is always JSON.
- `--all` and `--limit` are removed from lists, which read every page; `log` keeps `--limit`.
- The list envelope gains `kind` and loses `truncated`. `-` reads a list printed by the CLI.
- Token sources: `ENCLAVE_TOKEN`, then `credentials.json`. `--token` is removed.
- Organisation and partner precedence, including `ENCLAVE_ORG`, `ENCLAVE_ORG_ID` and `ENCLAVE_PARTNER_ID`.
- IDs only after `--id` or an `--<item>-id` option; `--<item>` options take names.
- `--yes` and its exit code are removed: changes run when given, and `--dry-run` previews them.
- Exit 6 for transient failures, and the fixed `code` set.
- The bulk output shape, empty stdin, bulk calls of 200 IDs, and the ID checks.
- A create sends every setting, with the values the CLI documents.
- Ranges, ACLs and gateway subnets take an optional `=label`, and an update keeps the labels of entries that stay.
- `commands [<noun> [<verb>]]` replaces `commands --json`.

## Build order

1. `login`, `status`, `org list`, `org use`: the token, context, output, error and exit-code handling every later command uses.
2. `list` and `show` for every top-level noun.
3. Changes, with `--dry-run`: `system`, `key`, `policy`, `tag`.
4. `dns`, `trust`, `org update`, `org list-users`, `org remove-user`, the invite commands, and the commands that wait on `Enclave.Sdk.Api` changes.
5. `partner customer`, once `Enclave.Sdk.Api` has partner clients.

## Examples

```sh
# 1. Log in from a saved token and work in the Acme organisation
enclave-cli login --token-stdin < ~/.secrets/enclave-token
enclave-cli org use Acme
enclave-cli org use --id 3f2a9c1e-7b4d-4e8a-9c61-2d5b8e0f4a17

# 2. In CI, list the connected Linux build servers in one organisation without touching the shared default
ENCLAVE_TOKEN="$CI_ENCLAVE_TOKEN" ENCLAVE_ORG=Acme enclave-cli system list --tag build --os linux --state connected
ENCLAVE_TOKEN="$CI_ENCLAVE_TOKEN" ENCLAVE_ORG_ID=3f2a9c1e-7b4d-4e8a-9c61-2d5b8e0f4a17 enclave-cli system list --tag build --os linux --state connected

# 3. Approve every system waiting that enrolled with key 12
enclave-cli system list --pending --key-id 12 | enclave-cli system approve -

# 4. Show a reviewer what that approval would send, without sending it
enclave-cli system list --pending --key-id 12 | enclave-cli system approve - --dry-run

# 5. Decline systems that have been waiting for approval for more than a week
enclave-cli system list --pending --waiting-for 7d | enclave-cli system decline -

# 6. Give contractors access for 8 hours through their policy
enclave-cli policy enable contractors --for 8h
enclave-cli policy enable --id 23 --for 8h

# 7. Let a visitor's laptop in for a day, then revoke it automatically
enclave-cli system enable K7P2Q --for 24h --then revoke

# 8. Make a key for CI runners, usable only from the CI network, that removes each runner's system 10 minutes after it disconnects, and keep its secret
ENROLMENT_KEY=$(enclave-cli key create "ci runners" --ephemeral --tags ci,runner --allow-ip 198.51.100.0/24 --keep-disconnected 10m | jq -r .key)

# 9. Rotate an enrolment key: create the replacement, then disable the old key
enclave-cli key create "build agents 2026-10" --tags build,linux && enclave-cli key disable "build agents 2026-04"
enclave-cli key create "build agents 2026-10" --tags build,linux && enclave-cli key disable --id 12

# 10. Add a tag to a system and keep the tags it has
enclave-cli system update ABCDE --add-tags monitoring

# 11. Let the API servers use the database policy too, checking the request first
enclave-cli policy update "web to db" --set-senders web,api --dry-run
enclave-cli policy update "web to db" --set-senders web,api
enclave-cli policy update --id 42 --set-senders web,api

# 12. Let a contractor's laptop in until 17:30 on Friday, then switch it off again
enclave-cli system enable M4R8T --until 2026-10-09T17:30

# 13. Delete every disabled enrolment key
enclave-cli key list --state disabled | enclave-cli key delete -

# 14. Revoke systems that have not connected for 90 days
enclave-cli system list --not-seen-for 90d | enclave-cli system revoke -

# 15. Point db.internal at the two database systems
enclave-cli dns create-hostname db.internal --systems ABCDE,FGHIJ --notes "primary database pair"

# 16. Remove every hostname for old-api, after checking which ones match
enclave-cli dns list-hostnames --filter old-api
enclave-cli dns list-hostnames --filter old-api | enclave-cli dns delete-hostname -

# 17. See every option system list takes, and the values each accepts
enclave-cli commands system list

# 18. Make a change in the Acme organisation, whatever the saved default is
enclave-cli policy disable "web to db" --org Acme
enclave-cli policy disable --id 42 --org-id 3f2a9c1e-7b4d-4e8a-9c61-2d5b8e0f4a17

# 19. Offboard a person: remove their account and cancel any invite still pending
enclave-cli org remove-user sam@example.com
enclave-cli org cancel-invite sam@example.com

# 20. See how many systems each customer has enrolled (each customer's enrolledSystems)
enclave-cli partner customer list

# 21. Let web servers reach the databases on PostgreSQL, checking the request first
enclave-cli policy create "web to db" --senders web --receivers db --acl tcp:5432 --dry-run
enclave-cli policy create "web to db" --senders web --receivers db --acl tcp:5432

# 22. Give support SSH access to production until 18:00, then disable the policy
enclave-cli policy create "support ssh" --senders support --receivers prod --acl tcp:22 --until 18:00

# 23. Open DNS and HTTPS from the office laptops to the build farm, only for staff signed in to Entra ID
enclave-cli policy create "office to build farm" --senders office,laptop --receivers build --acl udp:53 --acl tcp:443 --trust "entra staff"
enclave-cli policy create "office to build farm" --senders office,laptop --receivers build --acl udp:53 --acl tcp:443 --trust-id 3

# 24. Replace a policy's rules: build servers may now reach the artifact store on 443 and 8000-8100 only
enclave-cli policy update "build to artifacts" --set-acl tcp:443 --set-acl tcp:8000-8100
enclave-cli policy update --id 57 --set-acl tcp:443 --set-acl tcp:8000-8100

# 25. Make GW001 a gateway for the office LAN, then route staff traffic for it through GW001
enclave-cli system update GW001 --enable-gateway-for "10.0.0.0/16=Office LAN"
enclave-cli policy create "staff to office LAN" --senders staff --gateway GW001:10.0.0.0/16 --subnet-filter 10.0.0.0/16 --acl any

# 26. Require staff to be signed in to Entra ID and in the engineering group
enclave-cli trust create "entra staff" --authority azure --tenant 9b1c3a52-7f0e-4d8a-b0a4-2c6e1d5f8a31 --claim groups=4e2d8c1a-0b7f-4c39-9a65-1f3e7d2b6c84

# 27. Refuse connections from outside the UK and from one known bad range
enclave-cli trust create "uk only" --allow-country GB --block-ip 203.0.113.0/24

# 28. Every system tagged prod must also meet the "uk only" requirement
enclave-cli tag set prod --trust "uk only"
enclave-cli tag set prod --trust-id 5

# 29. Give each system tagged web a name in the internal zone automatically
enclave-cli dns create-zone internal --auto-dns-tags web

# 30. Allow the cleaning crew's tablets in only during office hours
enclave-cli policy create "facilities tablets" --senders tablets --receivers printers --acl tcp:9100 --active-hours "mon-fri 08:00-18:00 Europe/London"

# 31. Disable a policy by its description
enclave-cli policy disable "web to db"
enclave-cli policy disable --id 42

# 32. Stop a key enrolling new systems without approval
enclave-cli key update "build agents" --require-approval
enclave-cli key update --id 12 --require-approval

# 33. Show the last day's activity
enclave-cli log --since 24h

# 34. Remove a customer admin who has left the partner
enclave-cli partner customer remove-admin "Globex Ltd" --user alex@example.com

# 35. Disable several policies by ID in one call
enclave-cli policy disable --id 42,43,57

# 36. Check which token, organisation and partner the CLI will use
enclave-cli status

# 37. Find every gateway and the subnets it serves
enclave-cli system list --gateway

# 38. List systems, most recently connected first
enclave-cli system list --sort recently-connected

# 39. Find systems running an agent older than 2024.8.0, using the API's search syntax
enclave-cli system list --filter "version:<2024.8.0"

# 40. Find the Windows systems that are offline, and how many (total)
enclave-cli system list --os windows --state disconnected

# 41. Tag a system before approving it, then approve it
enclave-cli system update XYZ12 --pending --set-tags kiosk,lobby && enclave-cli system approve XYZ12

# 42. Take a tag off a system
enclave-cli system update ABCDE --remove-tags legacy

# 43. Stop GW001 acting as a gateway
enclave-cli system update GW001 --disable-gateway

# 44. Disable three systems at once
enclave-cli system disable ABCDE FGHIJ KLMNO

# 45. Disable every system listed in a file, one ID per line
enclave-cli system disable $(cat quarantine.txt)

# 46. Show an existing enrolment key with its secret
enclave-cli key show "build agents"
enclave-cli key show --id 12

# 47. List the keys that approve automatically and are tagged ci
enclave-cli key list --approval automatic --tag ci

# 48. Let a key enrol five more systems, from the office network only
enclave-cli key update "build agents" --uses 5 --set-allow-ip 203.0.113.0/24
enclave-cli key update --id 12 --uses 5 --set-allow-ip 203.0.113.0/24

# 49. Let a key enrol for two weeks, then delete it
enclave-cli key enable "contractor laptops" --for 14d --then delete
enclave-cli key enable --id 31 --for 14d --then delete

# 50. List the disabled policies
enclave-cli policy list --state disabled

# 51. Remove a policy's office-hours restriction
enclave-cli policy update "facilities tablets" --set-active-hours ""
enclave-cli policy update --id 61 --set-active-hours ""

# 52. Allow ping and a UDP port range between the monitoring and app servers
enclave-cli policy create "monitoring" --senders monitoring --receivers app --acl icmp --acl udp:8125-8126

# 53. Route the office LAN through two gateways: shared between them, through GW001 with GW002 as standby, or through whichever is closest
enclave-cli policy create "office LAN" --senders staff --gateway GW001:10.0.0.0/16 --gateway GW002:10.0.0.0/16 --mode balanced --acl any
enclave-cli policy create "office LAN" --senders staff --gateway GW001:10.0.0.0/16 --gateway GW002:10.0.0.0/16 --mode ordered --acl any
enclave-cli policy create "office LAN" --senders staff --gateway GW001:10.0.0.0/16 --gateway GW002:10.0.0.0/16 --mode geographic --acl any

# 54. See what deleting a policy would send, then delete it
enclave-cli policy delete "old vpn" --dry-run
enclave-cli policy delete "old vpn"
enclave-cli policy delete --id 17 --dry-run
enclave-cli policy delete --id 17

# 55. Rename a tag and give it a colour
enclave-cli tag set web --name frontend --colour "#2f80ed"

# 56. Require users to be signed in to the Enclave portal
enclave-cli trust create "portal login" --authority portal

# 57. Require a sign-in through a generic OIDC provider, from the example.com domain
enclave-cli trust create "sso" --authority oidc --authority-uri https://sso.example.com --claim hd=example.com

# 58. Allow Ireland as well as the UK; the blocked range is left as it is
enclave-cli trust update "uk only" --set-allow-country GB --set-allow-country IE
enclave-cli trust update --id 5 --set-allow-country GB --set-allow-country IE

# 59. List the IP-based trust requirements
enclave-cli trust list --type public-ip

# 60. Name systems tagged web or api in the internal zone automatically
enclave-cli dns update-zone internal --set-auto-dns-tags web,api
enclave-cli dns update-zone --id 4 --set-auto-dns-tags web,api

# 61. Add a third database system to db.internal
enclave-cli dns update-hostname db.internal --set-systems ABCDE,FGHIJ,KLMNO
enclave-cli dns update-hostname --id 7 --set-systems ABCDE,FGHIJ,KLMNO

# 62. List every hostname in the internal zone
enclave-cli dns list-hostnames --zone internal
enclave-cli dns list-hostnames --zone-id 4

# 63. Rename the organisation and set its contact details
enclave-cli org update --name "Acme Ltd" --website https://acme.example --phone "+44 20 7946 0000"

# 64. Invite a new administrator, then check the pending invites
enclave-cli org invite alex@example.com && enclave-cli org list-invites

# 65. As a partner, make one of your staff an admin of a customer and turn on auto-sync for that customer
enclave-cli partner customer add-admin "Globex Ltd" --user-id 5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25 && enclave-cli partner customer enable-auto-sync "Globex Ltd"
enclave-cli partner customer add-admin --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --user-id 5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25 && enclave-cli partner customer enable-auto-sync --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b

# 66. Invite someone at the customer to be its admin
enclave-cli partner customer invite "Globex Ltd" --email sam@globex.example

# 67. Sign out on a shared machine
enclave-cli logout

# 68. Show the latest activity
enclave-cli log

# 69. Show the last 1000 entries
enclave-cli log --limit 1000

# 70. See what one person changed this week
enclave-cli log --since 7d --user sam@example.com

# 71. Check the last hour for warnings and errors, from a scheduled job
enclave-cli log --since 1h --level warning,error

# 72. Find everything that happened to one system this month
enclave-cli log --since 30d --filter K7P2Q

# 73. See what happened during an incident window
enclave-cli log --since 2026-10-05T09:00 --until 2026-10-05T12:00

# 74. Label each rule so the portal shows what it is for; a label can contain spaces and commas
enclave-cli policy create "reports to db" --senders reports --receivers db --acl "tcp:5432=PostgreSQL" --acl "icmp=Ping, for monitoring"

# 75. Make GW001 a gateway for two subnets, each with a name
enclave-cli system update GW001 --enable-gateway-for "10.0.0.0/16=Office LAN" --enable-gateway-for "10.1.0.0/16=Warehouse, ground floor"

# 76. Restrict a key to two labelled networks
enclave-cli key update "build agents" --set-allow-ip "203.0.113.0/24=London office" --set-allow-ip "198.51.100.0/24=CI runners"
enclave-cli key update --id 12 --set-allow-ip "203.0.113.0/24=London office" --set-allow-ip "198.51.100.0/24=CI runners"

# 77. Add a range to a gateway policy; 10.0.0.0/16 is given without a label, so it keeps the label it has
enclave-cli policy update "staff to office LAN" --set-subnet-filter 10.0.0.0/16 --set-subnet-filter "10.2.0.0/16=New lab"
enclave-cli policy update --id 64 --set-subnet-filter 10.0.0.0/16 --set-subnet-filter "10.2.0.0/16=New lab"

# 78. Relabel one range and remove the label from another, keeping both ranges
enclave-cli policy update "staff to office LAN" --set-subnet-filter "10.0.0.0/16=Head office" --set-subnet-filter "10.2.0.0/16="

# 79. A label containing "=": the CLI splits at the first one, so the label is "VLAN=20 (finance)"
enclave-cli policy update "staff to office LAN" --set-subnet-filter "10.0.0.0/16=VLAN=20 (finance)" --set-subnet-filter 10.2.0.0/16

# 80. See the ranges and labels an update would send, kept labels included, without sending it
enclave-cli policy update "staff to office LAN" --set-subnet-filter 10.0.0.0/16 --set-subnet-filter 10.3.0.0/16 --dry-run

# 81. Replace a policy's rules and keep the labels on rules that stay; tcp:443 keeps its label, tcp:8443 has none
enclave-cli policy update "build to artifacts" --set-acl tcp:443 --set-acl tcp:8443

# 82. Allow the office range but not the guest Wi-Fi address inside it; the smaller range wins
enclave-cli trust create "office only" --allow-ip "203.0.113.0/24=Office" --block-ip "203.0.113.66/32=Guest Wi-Fi NAT"

# 83. Allow the UK and Ireland, each labelled, and block one range within them
enclave-cli trust create "uk and ie" --allow-country "GB=UK offices" --allow-country "IE=Dublin office" --block-ip "198.51.100.0/24=Shared hosting"

# 84. Block a country; the allowed countries, the blocked range and their labels are left as they are
enclave-cli trust update "uk and ie" --set-block-country "US=No US access"
enclave-cli trust update --id 9 --set-block-country "US=No US access"

# 85. As a partner, add a customer for about 20 systems and a gateway, owned by their IT lead, with your staff kept as admins
enclave-cli partner customer create "Initech" --owner it@initech.example --domain initech.example --systems 20 --gateways 1 --auto-sync

# 86. Move a customer from trial to paid, billed yearly
enclave-cli partner customer convert "Initech" --billing-months 12
enclave-cli partner customer convert --org-id 8d2e4f6a-1b3c-4d5e-9f70-a1b2c3d4e5f6 --billing-months 12
```
