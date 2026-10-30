# `enclave-cli org update`

Change the organisation's name, website or phone number, and print its properties.

## Synopsis

```text
enclave-cli org update [options]
```

## Example

```sh
enclave-cli org update --org 'Acme Ltd' --website https://acme.example
```

## Options

| Option | Use |
| --- | --- |
| `--name` | Set the item's name. |
| `--website` | Set the organisation's website. |
| `--phone` | Set the organisation's phone number. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
