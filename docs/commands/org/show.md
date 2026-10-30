# `enclave-cli org show`

Show the organisation's properties.

## Synopsis

```text
enclave-cli org show [options]
```

## Example

```sh
enclave-cli org show --org 'Acme Ltd'
```

## Options

| Option | Use |
| --- | --- |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

The resulting item as JSON.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
