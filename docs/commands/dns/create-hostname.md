# `enclave-cli dns create-hostname`

Give the full hostname. The CLI selects the longest matching DNS zone. `--tags` and `--systems` choose what the hostname resolves to.

## Synopsis

```text
enclave-cli dns create-hostname <hostname> [options]
```

## Example

```sh
enclave-cli dns create-hostname db.internal --systems ABCDE,FGHIJ
```

## Options

| Option | Use |
| --- | --- |
| `--tags` | Set the initial tags as a comma list. |
| `--systems` | Set system IDs the hostname answers for. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
