# `enclave-cli system enable`

`--for` or `--until` enables 1 system temporarily. `--then disable` is the default; `--then revoke` removes it when the time expires.

## Synopsis

```text
enclave-cli system enable <systemId> [options]
```

## Example

```sh
enclave-cli system enable ABCDE --for 8h --then revoke
```

## Options

| Option | Use |
| --- | --- |
| `--for` | Keep the change for a duration such as `8h`. |
| `--until` | Keep the change until this time. |
| `--then` | Choose what happens at expiry. Values: `disable`, `revoke`. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

A JSON result with `requested` and `affected`; a timed enable prints the updated item.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
