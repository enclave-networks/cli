# `enclave-cli dns create-zone`

Create a DNS zone and print it.

## Synopsis

```text
enclave-cli dns create-zone <zone> [options]
```

## Example

```sh
enclave-cli dns create-zone internal --auto-dns-tags servers
```

## Options

| Option | Use |
| --- | --- |
| `--auto-dns-tags` | Set tags used for automatic DNS entries. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
