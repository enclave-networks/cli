# `enclave-cli partner customer list-invites`

List the customer's pending admin invites.

## Synopsis

```text
enclave-cli partner customer list-invites <customer> [options]
```

## Example

```sh
enclave-cli partner customer list-invites Initech
```

Use the customer name or `--org-id <orgId>` to select the customer.

## Options

| Option | Use |
| --- | --- |
| `--org-id` | Select the customer by organisation ID. |
| `--partner-id` | Use this partner by ID. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
