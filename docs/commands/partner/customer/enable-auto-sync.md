# `enclave-cli partner customer enable-auto-sync`

Turn on auto-sync for the customer, which keeps the partner's staff as its admins.

## Synopsis

```text
enclave-cli partner customer enable-auto-sync <customer> [options]
```

## Example

```sh
enclave-cli partner customer enable-auto-sync Initech
```

Use the customer name or `--org-id <orgId>` to select the customer.

## Options

| Option | Use |
| --- | --- |
| `--org-id` | Select the customer by organisation ID. |
| `--partner-id` | Use this partner by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
