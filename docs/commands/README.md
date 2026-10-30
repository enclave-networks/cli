# Command reference

`enclave-cli` commands follow `enclave-cli <noun> <verb> [arguments] [options]`. Each page covers one command. Use `enclave-cli commands <command words>` for a machine-readable description of arguments, options and allowed values.

## Shared rules

- Select an organisation with `--org <name>` or `--org-id <id>`, or save one with [`org use`](org/use.md). Partner commands use [`partner use`](partner/use.md) or `--partner-id`.
- Use `--id` to select a named item by ID. Systems use IDs as arguments. A bulk command also accepts `-` to read matching list output from standard input.
- Changes take effect when given. Use `--dry-run` on API changes to inspect requests. Commands that change local files do not take it.
- Durations use minutes, hours or days: `30m`, `8h`, `7d`. Times without a zone use the machine's local time zone.
- `--set-` replaces a list; `--add-` and `--remove-` edit it. An empty string clears a setting only where the command accepts an empty value.
- Every successful command prints JSON to standard output. Errors print JSON to standard error. The output includes a `trace` when the command took a step worth tracing.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | API error or unimplemented command |
| 2 | Invalid argument, missing organisation or missing partner |
| 3 | Missing or invalid token |
| 4 | Forbidden by the API |
| 5 | Item not found |
| 6 | Network error, timeout, rate limit or server error |

## General

| Command | Purpose |
| --- | --- |
| [`enclave-cli login`](login.md) | Check a personal access token with one call and save it in ~/.enclave/credentials.json. |
| [`enclave-cli logout`](logout.md) | Delete ~/.enclave/credentials.json. |
| [`enclave-cli status`](status.md) | Show where the token comes from, the organisation and partner a command would use and what chose them, and the token's role in the organisation. |
| [`enclave-cli log`](log.md) | Print the organisation's activity log, newest first: the newest 100 entries, --limit entries, or every entry back to --since. |
| [`enclave-cli commands`](commands.md) | Describe every command, its arguments and options, the values each option allows, and the error codes with their exit codes, as JSON. |

## Org

| Command | Purpose |
| --- | --- |
| [`enclave-cli org list`](org/list.md) | List the organisations the token sees, with the token's role in each. |
| [`enclave-cli org use`](org/use.md) | Save the default organisation in ~/.enclave/cli.json, chosen by name or by --id; later commands use it with no lookup. |
| [`enclave-cli org show`](org/show.md) | Show the organisation's properties. |
| [`enclave-cli org update`](org/update.md) | Change the organisation's name, website or phone number, and print its properties. |
| [`enclave-cli org list-users`](org/list-users.md) | List the organisation's users. |
| [`enclave-cli org remove-user`](org/remove-user.md) | Remove a user from the organisation, given by email address or by --id. |
| [`enclave-cli org list-invites`](org/list-invites.md) | List the organisation's pending invites. |
| [`enclave-cli org invite`](org/invite.md) | Invite someone to the organisation by email address. |
| [`enclave-cli org cancel-invite`](org/cancel-invite.md) | Cancel the pending invite to an email address. |

## Partner

| Command | Purpose |
| --- | --- |
| [`enclave-cli partner use`](partner/use.md) | Save the default partner in ~/.enclave/cli.json; partner commands use it. |

## Partner customer

| Command | Purpose |
| --- | --- |
| [`enclave-cli partner customer list`](partner/customer/list.md) | List the partner's customers. |
| [`enclave-cli partner customer show`](partner/customer/show.md) | Show a customer. |
| [`enclave-cli partner customer create`](partner/customer/create.md) | Create a customer, sending every field of the API's customer model, with the partner portal's values for the flags left out. |
| [`enclave-cli partner customer update`](partner/customer/update.md) | Change a customer's name, contact, licensed systems and gateways, discount request and hard limit. |
| [`enclave-cli partner customer convert`](partner/customer/convert.md) | Convert a customer from trial to paid, with the billing period it commits to. |
| [`enclave-cli partner customer list-admins`](partner/customer/list-admins.md) | List the customer's admins. |
| [`enclave-cli partner customer add-admin`](partner/customer/add-admin.md) | Make one of the partner's users an admin of the customer, given by account ID, which the partner portal shows. |
| [`enclave-cli partner customer remove-admin`](partner/customer/remove-admin.md) | Remove an admin from the customer, given by email address or account ID. |
| [`enclave-cli partner customer list-invites`](partner/customer/list-invites.md) | List the customer's pending admin invites. |
| [`enclave-cli partner customer invite`](partner/customer/invite.md) | Invite someone to be an admin of the customer. |
| [`enclave-cli partner customer cancel-invite`](partner/customer/cancel-invite.md) | Cancel a pending admin invite to the customer, given by its email address. |
| [`enclave-cli partner customer enable-auto-sync`](partner/customer/enable-auto-sync.md) | Turn on auto-sync for the customer, which keeps the partner's staff as its admins. |
| [`enclave-cli partner customer disable-auto-sync`](partner/customer/disable-auto-sync.md) | Turn off auto-sync for the customer. |

## System

| Command | Purpose |
| --- | --- |
| [`enclave-cli system list`](system/list.md) | List the enrolled systems, or with --pending the systems waiting for approval, reading every page. |
| [`enclave-cli system show`](system/show.md) | Show a system, or with --pending a system waiting for approval. |
| [`enclave-cli system update`](system/update.md) | Change a system's description, notes, tags or gateway routes, or with --pending a waiting system's description, notes or tags, and print the system. |
| [`enclave-cli system approve`](system/approve.md) | Approve systems waiting for approval. |
| [`enclave-cli system decline`](system/decline.md) | Decline systems waiting for approval, removing them. |
| [`enclave-cli system enable`](system/enable.md) | Enable systems; with --for or --until, enable one system until then. |
| [`enclave-cli system disable`](system/disable.md) | Disable systems. |
| [`enclave-cli system revoke`](system/revoke.md) | Revoke systems, removing them from the organisation. |

## Key

| Command | Purpose |
| --- | --- |
| [`enclave-cli key list`](key/list.md) | List the enrolment keys, with their secrets, reading every page. |
| [`enclave-cli key show`](key/show.md) | Show a key, with its secret. |
| [`enclave-cli key create`](key/create.md) | Create an enrolment key, sending every setting, and print it with its secret. |
| [`enclave-cli key update`](key/update.md) | Change a key's settings and print the key. |
| [`enclave-cli key enable`](key/enable.md) | Enable keys; with --for or --until, enable one key until then. |
| [`enclave-cli key disable`](key/disable.md) | Disable keys, so no system can enrol with them. |
| [`enclave-cli key delete`](key/delete.md) | Delete keys. |

## Policy

| Command | Purpose |
| --- | --- |
| [`enclave-cli policy list`](policy/list.md) | List the policies, reading every page. |
| [`enclave-cli policy show`](policy/show.md) | Show a policy. |
| [`enclave-cli policy create`](policy/create.md) | Create a policy, sending every setting, and print it. |
| [`enclave-cli policy update`](policy/update.md) | Change a policy's settings and print the policy. |
| [`enclave-cli policy enable`](policy/enable.md) | Enable policies; with --for or --until, enable one policy until then. |
| [`enclave-cli policy disable`](policy/disable.md) | Disable policies. |
| [`enclave-cli policy delete`](policy/delete.md) | Delete policies. |

## Tag

| Command | Purpose |
| --- | --- |
| [`enclave-cli tag list`](tag/list.md) | List the tags, reading every page. |
| [`enclave-cli tag show`](tag/show.md) | Show a tag. |
| [`enclave-cli tag set`](tag/set.md) | Update a tag, creating it when it does not exist, which makes it permanent; print the tag. |
| [`enclave-cli tag delete`](tag/delete.md) | Delete tags. |

## Dns

| Command | Purpose |
| --- | --- |
| [`enclave-cli dns show`](dns/show.md) | Show the organisation's DNS summary. |
| [`enclave-cli dns list-zones`](dns/list-zones.md) | List the DNS zones, reading every page. |
| [`enclave-cli dns show-zone`](dns/show-zone.md) | Show a zone. |
| [`enclave-cli dns create-zone`](dns/create-zone.md) | Create a DNS zone and print it. |
| [`enclave-cli dns update-zone`](dns/update-zone.md) | Change a zone and print it. |
| [`enclave-cli dns delete-zone`](dns/delete-zone.md) | Delete a zone and print it. |
| [`enclave-cli dns list-hostnames`](dns/list-hostnames.md) | List the hostnames, reading every page. |
| [`enclave-cli dns show-hostname`](dns/show-hostname.md) | Show a hostname. |
| [`enclave-cli dns create-hostname`](dns/create-hostname.md) | Create a hostname in the zone its name ends in, and print it. |
| [`enclave-cli dns update-hostname`](dns/update-hostname.md) | Change a hostname and print it. |
| [`enclave-cli dns delete-hostname`](dns/delete-hostname.md) | Delete hostnames. |

## Trust

| Command | Purpose |
| --- | --- |
| [`enclave-cli trust list`](trust/list.md) | List the trust requirements, reading every page. |
| [`enclave-cli trust show`](trust/show.md) | Show a trust requirement. |
| [`enclave-cli trust create`](trust/create.md) | Create a trust requirement and print it: --authority makes a sign-in requirement, --allow-* and --block-* a public IP requirement. |
| [`enclave-cli trust update`](trust/update.md) | Change a trust requirement and print it. |
| [`enclave-cli trust delete`](trust/delete.md) | Delete trust requirements. |

For complete behaviour and defaults, see the [CLI specification](../../spec/cli-surface.md).
