# `enclave-cli key list`

The output includes each key's secret. Handle it as a credential. `--state disabled` includes disabled keys without `--include-disabled`.

## Synopsis

```text
enclave-cli key list [options]
```

## Example

```sh
enclave-cli key list --state disabled
```

## Options

| Option | Use |
| --- | --- |
| `--filter` | Search by text or the API's search syntax. |
| `--tag` | Filter by tags; a comma list requires every tag. |
| `--approval` | Filter by approval mode. Values: `automatic`, `manual`. |
| `--state` | Filter by state. Values: `enabled`, `disabled`, `no-uses`. |
| `--include-disabled` | Include disabled items. |
| `--sort` | Sort the matching items. Values: `description`, `last-used`, `approval-mode`, `uses-remaining`. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
