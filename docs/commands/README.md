# Command reference

`enclave-cli` commands follow `enclave-cli <noun> <verb> [arguments] [options]`. Each page covers one command. Use `enclave-cli commands <command words>` for a machine-readable description of arguments, options and allowed values.

## Shared rules

- Select an organisation with `--org <name>` or `--org-id <id>`, or save one with [`org use`](org/use.md). Partner commands take the partner ID with `--partner-id` or `ENCLAVE_CLI_PARTNER_ID`.
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
| [`enclave-cli login`](login.md) | Saves a personal access token and a default organisation. |
| [`enclave-cli logout`](logout.md) | Deletes ~/.enclave/credentials.json. |
| [`enclave-cli status`](status.md) | Shows the organisation and partner commands will use, and your role. |
| [`enclave-cli log`](log.md) | Shows the organisation's activity log. |
| [`enclave-cli commands`](commands.md) | Describes the commands as JSON. |

## Org

| Command | Purpose |
| --- | --- |
| [`enclave-cli org list`](org/list.md) | Lists the organisations the token sees. |
| [`enclave-cli org use`](org/use.md) | Saves the default organisation. |
| [`enclave-cli org show`](org/show.md) | Shows the organisation. |
| [`enclave-cli org update`](org/update.md) | Updates the organisation. |
| [`enclave-cli org list-users`](org/list-users.md) | Lists the organisation's users. |
| [`enclave-cli org remove-user`](org/remove-user.md) | Removes a user from the organisation. |
| [`enclave-cli org list-invites`](org/list-invites.md) | Lists the organisation's pending invites. |
| [`enclave-cli org invite`](org/invite.md) | Invites someone to the organisation. |
| [`enclave-cli org cancel-invite`](org/cancel-invite.md) | Cancels a pending invite. |

## Partner customer

| Command | Purpose |
| --- | --- |
| [`enclave-cli partner customer list`](partner/customer/list.md) | Lists the partner's customers. |
| [`enclave-cli partner customer show`](partner/customer/show.md) | Shows a customer. |
| [`enclave-cli partner customer create`](partner/customer/create.md) | Creates a customer. |
| [`enclave-cli partner customer update`](partner/customer/update.md) | Updates a customer. |
| [`enclave-cli partner customer convert`](partner/customer/convert.md) | Converts a customer from trial to paid. |
| [`enclave-cli partner customer list-admins`](partner/customer/list-admins.md) | Lists a customer's admins. |
| [`enclave-cli partner customer add-admin`](partner/customer/add-admin.md) | Adds an admin to a customer. |
| [`enclave-cli partner customer remove-admin`](partner/customer/remove-admin.md) | Removes an admin from a customer. |
| [`enclave-cli partner customer list-invites`](partner/customer/list-invites.md) | Lists a customer's pending admin invites. |
| [`enclave-cli partner customer invite`](partner/customer/invite.md) | Invites someone to be an admin of a customer. |
| [`enclave-cli partner customer cancel-invite`](partner/customer/cancel-invite.md) | Cancels a pending admin invite to a customer. |
| [`enclave-cli partner customer enable-auto-sync`](partner/customer/enable-auto-sync.md) | Turns on auto-sync for a customer. |
| [`enclave-cli partner customer disable-auto-sync`](partner/customer/disable-auto-sync.md) | Turns off auto-sync for a customer. |

## System

| Command | Purpose |
| --- | --- |
| [`enclave-cli system list`](system/list.md) | Lists systems. |
| [`enclave-cli system show`](system/show.md) | Shows a system. |
| [`enclave-cli system update`](system/update.md) | Updates a system. |
| [`enclave-cli system approve`](system/approve.md) | Approves systems waiting for approval. |
| [`enclave-cli system decline`](system/decline.md) | Declines systems waiting for approval. |
| [`enclave-cli system enable`](system/enable.md) | Enables systems. |
| [`enclave-cli system disable`](system/disable.md) | Disables systems. |
| [`enclave-cli system revoke`](system/revoke.md) | Revokes systems. |

## Key

| Command | Purpose |
| --- | --- |
| [`enclave-cli key list`](key/list.md) | Lists enrolment keys. |
| [`enclave-cli key show`](key/show.md) | Shows an enrolment key. |
| [`enclave-cli key create`](key/create.md) | Creates an enrolment key. |
| [`enclave-cli key update`](key/update.md) | Updates an enrolment key. |
| [`enclave-cli key enable`](key/enable.md) | Enables enrolment keys. |
| [`enclave-cli key disable`](key/disable.md) | Disables enrolment keys. |
| [`enclave-cli key delete`](key/delete.md) | Deletes enrolment keys. |

## Policy

| Command | Purpose |
| --- | --- |
| [`enclave-cli policy list`](policy/list.md) | Lists policies. |
| [`enclave-cli policy show`](policy/show.md) | Shows a policy. |
| [`enclave-cli policy create`](policy/create.md) | Creates a policy. |
| [`enclave-cli policy update`](policy/update.md) | Updates a policy. |
| [`enclave-cli policy enable`](policy/enable.md) | Enables policies. |
| [`enclave-cli policy disable`](policy/disable.md) | Disables policies. |
| [`enclave-cli policy delete`](policy/delete.md) | Deletes policies. |

## Tag

| Command | Purpose |
| --- | --- |
| [`enclave-cli tag list`](tag/list.md) | Lists tags. |
| [`enclave-cli tag show`](tag/show.md) | Shows a tag. |
| [`enclave-cli tag set`](tag/set.md) | Creates or updates a tag. |
| [`enclave-cli tag delete`](tag/delete.md) | Deletes tags. |

## Dns

| Command | Purpose |
| --- | --- |
| [`enclave-cli dns show`](dns/show.md) | Shows the organisation's DNS summary. |
| [`enclave-cli dns list-zones`](dns/list-zones.md) | Lists DNS zones. |
| [`enclave-cli dns show-zone`](dns/show-zone.md) | Shows a DNS zone. |
| [`enclave-cli dns create-zone`](dns/create-zone.md) | Creates a DNS zone. |
| [`enclave-cli dns update-zone`](dns/update-zone.md) | Updates a DNS zone. |
| [`enclave-cli dns delete-zone`](dns/delete-zone.md) | Deletes a DNS zone. |
| [`enclave-cli dns list-hostnames`](dns/list-hostnames.md) | Lists hostnames. |
| [`enclave-cli dns show-hostname`](dns/show-hostname.md) | Shows a hostname. |
| [`enclave-cli dns create-hostname`](dns/create-hostname.md) | Creates a hostname. |
| [`enclave-cli dns update-hostname`](dns/update-hostname.md) | Updates a hostname. |
| [`enclave-cli dns delete-hostname`](dns/delete-hostname.md) | Deletes hostnames. |

## Trust

| Command | Purpose |
| --- | --- |
| [`enclave-cli trust list`](trust/list.md) | Lists trust requirements. |
| [`enclave-cli trust show`](trust/show.md) | Shows a trust requirement. |
| [`enclave-cli trust create`](trust/create.md) | Creates a trust requirement. |
| [`enclave-cli trust update`](trust/update.md) | Updates a trust requirement. |
| [`enclave-cli trust delete`](trust/delete.md) | Deletes trust requirements. |

For complete behaviour and defaults, see the [CLI specification](../../spec/cli-surface.md).
