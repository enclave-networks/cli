# `enclave-cli key update`

`--set-tags` replaces all tags. `--add-tags` and `--remove-tags` edit the existing list. `--set-allow-ip` replaces allowed ranges; repeat it for more than 1 range.

## Synopsis

```text
enclave-cli key update <key> [options]
```

## Example

```sh
enclave-cli key update 'Leeds office' --add-tags production
```

Use the name argument or `--id <id>` to select the item.

## Options

| Option | Use |
| --- | --- |
| `--id` | Select the item by ID. |
| `--description` | Set the item's description. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--auto-approve` | Approve enrolment automatically. |
| `--require-approval` | Require manual approval for enrolment. |
| `--uses` | Set how many enrolments remain. |
| `--set-tags` | Replace the tags setting or list. |
| `--add-tags` | Add tags. |
| `--remove-tags` | Remove tags. |
| `--set-allow-ip` | Replace the allow ip setting or list. |
| `--keep-disconnected` | Keep disconnected ephemeral systems for this duration. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
