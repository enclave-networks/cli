# `enclave-cli partner customer add-admin`

`--user-id` is the partner user's account ID, shown in the partner portal.

## Synopsis

```text
enclave-cli partner customer add-admin <customer> [options]
```

## Example

```sh
enclave-cli partner customer add-admin Initech --user-id 8d2e4f6a-1b3c-4d5e-9f70-a1b2c3d4e5f6
```

Use the customer name or `--org-id <orgId>` to select the customer.

## Options

| Option | Use |
| --- | --- |
| `--org-id` | Select the customer by organisation ID. |
| `--user-id` | Select the account by ID. |
| `--partner-id` | Use this partner by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
