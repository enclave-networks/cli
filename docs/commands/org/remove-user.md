# `enclave-cli org remove-user`

Remove a user from the organisation, given by email address or by --id.

## Synopsis

```text
enclave-cli org remove-user <email> [options]
```

## Example

```sh
enclave-cli org remove-user former@acme.example --org 'Acme Ltd' --dry-run
```

Use the name argument or `--id <id>` to select the item.

## Options

| Option | Use |
| --- | --- |
| `--id` | Select the item by ID. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
