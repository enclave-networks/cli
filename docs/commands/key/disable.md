# `enclave-cli key disable`

Disable keys, so no system can enrol with them. Takes key descriptions, IDs after --id, or "-" to read a list from stdin, and prints { requested, affected }.

## Synopsis

```text
enclave-cli key disable <key> [options]
```

## Example

```sh
enclave-cli key disable 'Leeds office'
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

A JSON result with `requested` and `affected`.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
