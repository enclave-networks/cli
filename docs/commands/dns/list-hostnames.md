# `enclave-cli dns list-hostnames`

List the hostnames, reading every page.

## Synopsis

```text
enclave-cli dns list-hostnames [options]
```

## Example

```sh
enclave-cli dns list-hostnames --zone internal
```

## Options

| Option | Use |
| --- | --- |
| `--zone` | Filter hostnames by zone name. |
| `--zone-id` | Filter by DNS zone ID. |
| `--filter` | Search by text or the API's search syntax. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
