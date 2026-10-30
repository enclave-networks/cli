# `enclave-cli key create`

General-purpose keys require manual approval and have unlimited uses unless flags change those settings. `--ephemeral` creates a key with automatic approval, unlimited uses and a 30-minute disconnected-system retention. The output includes the key's secret.

## Synopsis

```text
enclave-cli key create <description> [options]
```

## Example

```sh
enclave-cli key create 'Leeds office' --tags leeds,office --uses 20
```

## Options

| Option | Use |
| --- | --- |
| `--ephemeral` | Create an ephemeral enrolment key. |
| `--auto-approve` | Approve enrolment automatically. |
| `--uses` | Set how many enrolments remain. |
| `--tags` | Set the initial tags as a comma list. |
| `--allow-ip` | Allow an IP range; repeat for more ranges. |
| `--keep-disconnected` | Keep disconnected ephemeral systems for this duration. |
| `--for` | Keep the change for a duration such as `8h`. |
| `--until` | Keep the change until this time. |
| `--then` | Choose what happens at expiry. Values: `disable`, `delete`. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
