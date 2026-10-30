# `enclave-cli policy update`

Each `--set-` option replaces its collection. Repeat `--set-acl` for every allowed protocol and port. `--set-active-hours ""` removes the hours restriction.

## Synopsis

```text
enclave-cli policy update <policy> [options]
```

## Example

```sh
enclave-cli policy update 'laptops to file servers' --set-acl tcp:445 --set-acl tcp:139
```

Use the name argument or `--id <id>` to select the item.

## Options

| Option | Use |
| --- | --- |
| `--id` | Select the item by ID. |
| `--description` | Set the item's description. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--set-senders` | Replace the senders setting or list. |
| `--set-receivers` | Replace the receivers setting or list. |
| `--set-acl` | Replace the acl setting or list. |
| `--set-trust` | Replace the trust setting or list. |
| `--set-trust-id` | Replace the trust id setting or list. |
| `--set-gateway` | Replace the gateway setting or list. |
| `--mode` | Choose how a gateway is selected. Values: `balanced`, `ordered`, `geographic`. |
| `--set-subnet-filter` | Replace the subnet filter setting or list. |
| `--set-active-hours` | Replace the active hours setting or list. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
