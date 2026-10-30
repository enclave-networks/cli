# `enclave-cli org list-users`

List the organisation's users.

## Synopsis

```text
enclave-cli org list-users [options]
```

## Example

```sh
enclave-cli org list-users --org 'Acme Ltd'
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
