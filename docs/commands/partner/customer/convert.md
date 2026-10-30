# `enclave-cli partner customer convert`

`--billing-months` is required. Accepted values are `1`, `12`, `24` and `36`.

## Synopsis

```text
enclave-cli partner customer convert <customer> [options]
```

## Example

```sh
enclave-cli partner customer convert Initech --billing-months 12
```

Use the customer name or `--org-id <orgId>` to select the customer.

## Options

| Option | Use |
| --- | --- |
| `--org-id` | Select the customer by organisation ID. |
| `--billing-months` | Set the committed billing period in months. Values: `1`, `12`, `24`, `36`. |
| `--partner-id` | Use this partner by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
