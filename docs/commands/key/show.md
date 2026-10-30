# `enclave-cli key show`

The output includes the key's secret. Use `--id <id>` to avoid a name lookup.

## Synopsis

```text
enclave-cli key show <key> [options]
```

## Example

```sh
enclave-cli key show 'Leeds office'
```

Use the name argument or `--id <id>` to select the item.

## Options

| Option | Use |
| --- | --- |
| `--id` | Select the item by ID. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

The resulting item as JSON.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
