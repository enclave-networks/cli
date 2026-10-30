# `enclave-cli partner customer list`

List the partner's customers.

## Synopsis

```text
enclave-cli partner customer list [options]
```

## Example

```sh
enclave-cli partner customer list
```

## Options

| Option | Use |
| --- | --- |
| `--partner-id` | Use this partner by ID. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
