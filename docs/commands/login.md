# `enclave-cli login`

The token comes from standard input with `--token-stdin`, or from `ENCLAVE_TOKEN`. The command checks it before saving `~/.enclave/credentials.json`. If the token sees exactly 1 organisation, that organisation becomes the default.

## Synopsis

```text
enclave-cli login [options]
```

## Example

```sh
enclave-cli login --token-stdin < ~/.secrets/enclave-token
```

## Options

| Option | Use |
| --- | --- |
| `--token-stdin` | Read the token from standard input. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](README.md#exit-codes).

See the [command index](README.md) for shared input rules.
