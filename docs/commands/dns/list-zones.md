# `enclave-cli dns list-zones`

List the DNS zones, reading every page.

## Synopsis

```text
enclave-cli dns list-zones [options]
```

## Example

```sh
enclave-cli dns list-zones
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
