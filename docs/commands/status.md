# `enclave-cli status`

The output names the token source and the selected organisation and partner. It also shows the token's role in the organisation. Use it to check context before a change.

## Synopsis

```text
enclave-cli status [options]
```

## Example

```sh
enclave-cli status --org 'Acme Ltd'
```

## Options

| Option | Use |
| --- | --- |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--partner-id` | Use this partner by ID. |

## Output

JSON with the token source, organisation, partner and role.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](README.md#exit-codes).

See the [command index](README.md) for shared input rules.
