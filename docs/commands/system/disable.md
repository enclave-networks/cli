# `enclave-cli system disable`

Disable systems. Takes any number of system IDs, or "-" to read a list from stdin, and prints { requested, affected }.

## Synopsis

```text
enclave-cli system disable <systemId> [options]
```

## Example

```sh
enclave-cli system disable ABCDE FGHIJ
```

## Options

| Option | Use |
| --- | --- |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

A JSON result with `requested` and `affected`.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
