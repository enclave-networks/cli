# `enclave-cli partner use`

Get the partner ID from the partner portal. The default is saved in `~/.enclave/cli.json`; `--partner-id` overrides it for one command.

## Synopsis

```text
enclave-cli partner use [options]
```

## Example

```sh
enclave-cli partner use --id 8d2e4f6a-1b3c-4d5e-9f70-a1b2c3d4e5f6
```

## Options

| Option | Use |
| --- | --- |
| `--id` | Set the default partner by ID. |

## Output

JSON with the selected partner ID.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
