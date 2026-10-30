# `enclave-cli org cancel-invite`

Cancel the pending invite to an email address.

## Synopsis

```text
enclave-cli org cancel-invite <email> [options]
```

## Example

```sh
enclave-cli org cancel-invite new@acme.example --org 'Acme Ltd'
```

## Options

| Option | Use |
| --- | --- |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
