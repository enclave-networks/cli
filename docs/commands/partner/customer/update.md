# `enclave-cli partner customer update`

Change a customer's name, contact, licensed systems and gateways, discount request and hard limit.

## Synopsis

```text
enclave-cli partner customer update <customer> [options]
```

## Example

```sh
enclave-cli partner customer update Initech --systems 25 --gateways 2
```

Use the customer name or `--org-id <orgId>` to select the customer.

## Options

| Option | Use |
| --- | --- |
| `--org-id` | Select the customer by organisation ID. |
| `--name` | Set the item's name. |
| `--contact` | Set the customer's contact name. |
| `--systems` | Set the licensed system count, or the hostname's system IDs. |
| `--gateways` | Set the licensed gateway count. |
| `--industry-discount` | Request an industry discount. |
| `--no-industry-discount` | Turn off industry discount. |
| `--hard-limit` | Apply a hard licence limit. |
| `--no-hard-limit` | Turn off hard limit. |
| `--partner-id` | Use this partner by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
