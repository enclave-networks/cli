# `enclave-cli partner customer invite`

Invite someone to be an admin of the customer.

## Synopsis

```text
enclave-cli partner customer invite <customer> [options]
```

## Example

```sh
enclave-cli partner customer invite Initech --email admin@initech.example
```

Use the customer name or `--org-id <orgId>` to select the customer.

## Options

| Option | Use |
| --- | --- |
| `--org-id` | Select the customer by organisation ID. |
| `--email` | Select the invite by email address. |
| `--partner-id` | Use this partner by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
