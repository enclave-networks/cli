# `enclave-cli policy enable`

`--for` or `--until` enables 1 policy temporarily. `--then disable` is the default; `--then delete` deletes it at expiry.

## Synopsis

```text
enclave-cli policy enable <policy> [options]
```

## Example

```sh
enclave-cli policy enable contractors --for 8h
```

Use the name argument or `--id <id>` to select the item.

## Options

| Option | Use |
| --- | --- |
| `--id` | Select the item by ID. |
| `--for` | Keep the change for a duration such as `8h`. |
| `--until` | Keep the change until this time. |
| `--then` | Choose what happens at expiry. Values: `disable`, `delete`. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

A JSON result with `requested` and `affected`; a timed enable prints the updated item.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
