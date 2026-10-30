# `enclave-cli trust list`

List the trust requirements, reading every page.

## Synopsis

```text
enclave-cli trust list [options]
```

## Example

```sh
enclave-cli trust list --type public-ip
```

## Options

| Option | Use |
| --- | --- |
| `--filter` | Search by text or the API's search syntax. |
| `--type` | Filter by type. Values: `user-auth`, `public-ip`. |
| `--sort` | Sort the matching items. Values: `description`, `recently-created`. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
