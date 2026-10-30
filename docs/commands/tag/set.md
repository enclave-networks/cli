# `enclave-cli tag set`

The command updates an existing tag or creates a permanent tag. Provide at least 1 change flag. `--trust` or `--trust-id` replaces the tag's trust requirements.

## Synopsis

```text
enclave-cli tag set <tag> [options]
```

## Example

```sh
enclave-cli tag set office --colour '#336699'
```

## Options

| Option | Use |
| --- | --- |
| `--name` | Set the item's name. |
| `--colour` | Set the tag's hex colour. |
| `--trust` | Select trust requirements by description. |
| `--trust-id` | Select trust requirements by ID. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
