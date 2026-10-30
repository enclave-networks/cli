# `enclave-cli log`

The default is the newest 100 entries. `--since` reads back to a time; `--limit` caps the result. `--user`, `--level` and `--filter` filter matching entries.

## Synopsis

```text
enclave-cli log [options]
```

## Example

```sh
enclave-cli log --since 24h --level warning,error
```

## Options

| Option | Use |
| --- | --- |
| `--limit` | Limit the number of entries printed. |
| `--since` | Start at this past time or duration. |
| `--until` | Leave out entries newer than this time. |
| `--user` | Select the user by email address. |
| `--level` | Filter by log level; comma lists are accepted. Values: `information`, `warning`, `error`. |
| `--filter` | Search by text or the API's search syntax. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

A JSON list of activity entries, newest first.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](README.md#exit-codes).

See the [command index](README.md) for shared input rules.
