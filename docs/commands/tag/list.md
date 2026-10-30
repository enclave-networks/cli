# `enclave-cli tag list`

List the tags, reading every page.

## Synopsis

```text
enclave-cli tag list [options]
```

## Example

```sh
enclave-cli tag list --filter office
```

## Options

| Option | Use |
| --- | --- |
| `--filter` | Search by text or the API's search syntax. |
| `--sort` | Sort the matching items. Values: `alphabetical`, `recently-used`, `referenced-systems`. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
