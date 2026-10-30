# `enclave-cli system revoke`

Revoking removes a system from the organisation. `system list --not-seen-for 90d` selects disconnected systems for a cleanup pipeline.

## Synopsis

```text
enclave-cli system revoke <systemId> [options]
```

## Example

```sh
enclave-cli system list --not-seen-for 90d | enclave-cli system revoke - --dry-run
```

## Options

| Option | Use |
| --- | --- |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

A JSON result with `requested` and `affected`.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
