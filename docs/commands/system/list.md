# `enclave-cli system list`

Use `--pending` to list systems waiting for approval. `--not-seen-for` selects disconnected systems whose last seen time, or enrolment time if never seen, is older than the duration. List output can feed bulk commands through `-`.

## Synopsis

```text
enclave-cli system list [options]
```

## Example

```sh
enclave-cli system list --state disconnected --not-seen-for 90d
```

## Options

| Option | Use |
| --- | --- |
| `--filter` | Search by text or the API's search syntax. |
| `--tag` | Filter by tags; a comma list requires every tag. |
| `--state` | Filter by state. Values: `connected`, `disconnected`. |
| `--os` | Filter by operating system. Values: `windows`, `linux`, `mac`. |
| `--type` | Filter by type. Values: `general`, `ephemeral`. |
| `--gateway` | List gateway systems only. |
| `--key` | Filter by enrolment key description. |
| `--key-id` | Filter by enrolment key ID. |
| `--not-seen-for` | Select disconnected systems older than this duration. |
| `--include-disabled` | Include disabled items. |
| `--sort` | Sort the matching items. Values: `recently-enrolled`, `recently-connected`, `description`, `description-or-hostname`, `enrolment-key-used`, `last-connected`, `state`, `version`. |
| `--pending` | Use systems waiting for approval. |
| `--waiting-for` | Select pending systems older than this duration. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](../README.md#exit-codes).

See the [command index](../README.md) for shared input rules.
