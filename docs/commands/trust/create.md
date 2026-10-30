# `enclave-cli trust create`

Use `--authority` for a sign-in requirement. Use `--allow-ip`, `--block-ip`, `--allow-country` or `--block-country` for a public IP requirement. Repeat a condition flag to add entries.

## Synopsis

```text
enclave-cli trust create <description> [options]
```

## Example

```sh
enclave-cli trust create 'office IPs' --allow-ip '203.0.113.0/24=Office'
```

## Options

| Option | Use |
| --- | --- |
| `--notes` | Set notes; use an empty string to clear them on update. |
| `--authority` | Choose the sign-in authority. Values: `portal`, `azure`, `google`, `okta`, `jumpcloud`, `duo`, `oidc`. |
| `--tenant` | Set the Azure tenant ID. |
| `--authority-uri` | Set the authority URL. |
| `--client-id` | Set the identity provider's client ID. |
| `--audience` | Set the expected audience. |
| `--claim` | Require a claim and value; repeat for more claims. |
| `--allow-ip` | Allow an IP range; repeat for more ranges. |
| `--block-ip` | Block an IP range; repeat for more ranges. |
| `--allow-country` | Allow a country code; repeat for more countries. |
| `--block-country` | Block a country code; repeat for more countries. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |
| `--dry-run` | Print the API request without sending the change. |

## Output

The resulting item as JSON.

Add `--dry-run` to inspect the request before making a change.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
