# `enclave-cli trust update`

Each `--set-` option replaces conditions of its own kind. Other kinds stay as they are. To change an authority, create a new requirement and delete the old one.

## Synopsis

```text
enclave-cli trust update <trust> [options]
```

## Example

```sh
enclave-cli trust update 'office IPs' --set-allow-ip '203.0.113.0/24=Office'
```

Use the name argument or `--id <id>` to select the item.

## Options

| Option | Use |
| --- | --- |
| `--id` | Select the item by ID. |
| `--description` | Set the item's description. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--set-tenant` | Replace the tenant setting or list. |
| `--set-authority-uri` | Replace the authority uri setting or list. |
| `--set-client-id` | Replace the client id setting or list. |
| `--set-audience` | Replace the audience setting or list. |
| `--set-claim` | Replace the claim setting or list. |
| `--set-allow-ip` | Replace the allow ip setting or list. |
| `--set-block-ip` | Replace the block ip setting or list. |
| `--set-allow-country` | Replace the allow country setting or list. |
| `--set-block-country` | Replace the block country setting or list. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
