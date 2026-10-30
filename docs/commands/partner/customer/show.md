# `enclave-cli partner customer show`

Show a customer.

## Synopsis

```text
enclave-cli partner customer show <customer> [options]
```

## Example

```sh
enclave-cli partner customer show Initech
```

Use the customer name or `--org-id <orgId>` to select the customer.

## Options

| Option | Use |
| --- | --- |
| `--org-id` | Select the customer by organisation ID. |
| `--partner-id` | Use this partner by ID. |

## Output

The resulting item as JSON.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
