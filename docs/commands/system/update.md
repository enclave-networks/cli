# `enclave-cli system update`

`--set-tags` replaces tags; `--add-tags` and `--remove-tags` edit them. `--enable-gateway-for` replaces user-entered gateway routes. Add `--pending` to edit a waiting system; gateway options apply only to enrolled systems.

## Synopsis

```text
enclave-cli system update <systemId> [options]
```

## Example

```sh
enclave-cli system update ABCDE --add-tags office
```

## Options

| Option | Use |
| --- | --- |
| `--description` | Set the item's description. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--set-tags` | Replace the tags setting or list. |
| `--add-tags` | Add tags. |
| `--remove-tags` | Remove tags. |
| `--enable-gateway-for` | Replace user-entered gateway routes; repeat for more subnets. |
| `--disable-gateway` | Stop using the system as a gateway. |
| `--pending` | Use systems waiting for approval. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
