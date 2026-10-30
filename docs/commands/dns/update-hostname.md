# `enclave-cli dns update-hostname`

`--name` renames the full hostname within the same zone. `--set-tags` and `--set-systems` replace their lists; `--add-tags` and `--remove-tags` edit tags.

## Synopsis

```text
enclave-cli dns update-hostname <hostname> [options]
```

## Example

```sh
enclave-cli dns update-hostname db.internal --add-tags database
```

Use the name argument or `--id <id>` to select the item.

## Options

| Option | Use |
| --- | --- |
| `--id` | Select the item by ID. |
| `--name` | Set the item's name. |
| `--set-tags` | Replace the tags setting or list. |
| `--add-tags` | Add tags. |
| `--remove-tags` | Remove tags. |
| `--set-systems` | Replace the systems setting or list. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
