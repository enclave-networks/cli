# `enclave-cli dns show-zone`

Show a zone.

## Synopsis

```text
enclave-cli dns show-zone <zone> [options]
```

## Example

```sh
enclave-cli dns show-zone internal
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
