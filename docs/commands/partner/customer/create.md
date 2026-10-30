# `enclave-cli partner customer create`

Set the owner and domain when creating the customer. `--systems` and `--gateways` set the licensed counts; `--auto-sync` keeps partner staff as customer admins.

## Synopsis

```text
enclave-cli partner customer create <name> [options]
```

## Example

```sh
enclave-cli partner customer create Initech --owner it@initech.example --domain initech.example --systems 20 --gateways 1
```

## Options

| Option | Use |
| --- | --- |
| `--owner` | Set the customer's owner email address. |
| `--domain` | Set the customer's domain. |
| `--contact` | Set the customer's contact name. |
| `--systems` | Set the licensed system count, or the hostname's system IDs. |
| `--gateways` | Set the licensed gateway count. |
| `--industry-discount` | Request an industry discount. |
| `--hard-limit` | Apply a hard licence limit. |
| `--auto-sync` | Keep partner staff as customer admins. |
| `--partner-id` | Use this partner by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../../README.md#exit-codes).

See the [command index](../../README.md) for shared input rules.
