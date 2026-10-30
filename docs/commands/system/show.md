# `enclave-cli system show`

Add `--pending` for a system waiting for approval. System IDs contain letters and digits; the CLI reads them in upper case.

## Synopsis

```text
enclave-cli system show <systemId> [options]
```

## Example

```sh
enclave-cli system show ABCDE
```

## Options

| Option | Use |
| --- | --- |
| `--pending` | Use systems waiting for approval. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

The resulting item as JSON.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
