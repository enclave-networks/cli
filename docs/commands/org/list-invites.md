# `enclave-cli org list-invites`

List the organisation's pending invites.

## Synopsis

```text
enclave-cli org list-invites [options]
```

## Example

```sh
enclave-cli org list-invites --org 'Acme Ltd'
```

## Options

| Option | Use |
| --- | --- |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
