# `enclave-cli partner customer remove-admin`

Remove an admin from the customer, given by email address or account ID.

## Synopsis

```text
enclave-cli partner customer remove-admin <customer> [options]
```

## Example

```sh
enclave-cli partner customer remove-admin Initech --user admin@initech.example
```

Use the customer name or `--org-id <orgId>` to select the customer.

## Options

| Option | Use |
| --- | --- |
| `--org-id` | Select the customer by organisation ID. |
| `--user` | Select the user by email address. |
| `--user-id` | Select the account by ID. |
| `--partner-id` | Use this partner by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
