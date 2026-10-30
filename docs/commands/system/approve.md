# `enclave-cli system approve`

The input must come from `system list --pending` when using `-`. A bulk response reports `requested` and `affected`; a smaller affected count means some systems were unknown or already approved.

## Synopsis

```text
enclave-cli system approve <systemId> [options]
```

## Example

```sh
enclave-cli system list --pending | enclave-cli system approve -
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
