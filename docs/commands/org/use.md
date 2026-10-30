# `enclave-cli org use`

The default is saved in `~/.enclave/cli.json`. Use `--id <orgId>` when a name is ambiguous. A command's `--org` or `--org-id` overrides the default.

## Synopsis

```text
enclave-cli org use <name> [options]
```

## Example

```sh
enclave-cli org use 'Acme Ltd'
```

Use the name argument or `--id <id>` to select the item.

## Options

| Option | Use |
| --- | --- |
| `--id` | Select the item by ID. |

## Output

The selected organisation as JSON.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
