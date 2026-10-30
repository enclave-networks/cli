# `enclave-cli policy list`

List the policies, reading every page.

## Synopsis

```text
enclave-cli policy list [options]
```

## Example

```sh
enclave-cli policy list --tag laptops --state enabled
```

## Options

| Option | Use |
| --- | --- |
| `--filter` | Search by text or the API's search syntax. |
| `--tag` | Filter by tags; a comma list requires every tag. |
| `--state` | Filter by state. Values: `enabled`, `disabled`. |
| `--include-disabled` | Include disabled items. |
| `--sort` | Sort the matching items. Values: `description`, `recently-created`. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
