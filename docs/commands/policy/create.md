# `enclave-cli policy create`

At least 1 `--acl` is required. `--receivers` makes a system-to-system policy; `--gateway SYSTEM:ROUTE` makes a gateway policy. `--mode` and `--subnet-filter` apply to gateway policies. `--disabled` creates the policy without enabling it.

## Synopsis

```text
enclave-cli policy create <description> [options]
```

## Example

```sh
enclave-cli policy create 'laptops to file servers' --senders laptops --receivers file-servers --acl tcp:445
```

## Options

| Option | Use |
| --- | --- |
| `--senders` | Set sender tags as a comma list. |
| `--receivers` | Set receiver tags as a comma list. |
| `--acl` | Allow a protocol and optional port or range; repeat for more rules. |
| `--trust` | Select trust requirements by description. |
| `--trust-id` | Select trust requirements by ID. |
| `--gateway` | Use a gateway system and route; repeat for more gateways. |
| `--mode` | Choose how a gateway is selected. Values: `balanced`, `ordered`, `geographic`. |
| `--subnet-filter` | Limit addresses reached through a gateway. |
| `--active-hours` | Limit when the policy is active. |
| `--for` | Keep the change for a duration such as `8h`. |
| `--until` | Keep the change until this time. |
| `--then` | Choose what happens at expiry. Values: `disable`, `delete`. |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--disabled` | Create the policy disabled. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
