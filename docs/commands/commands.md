# `enclave-cli commands`

With no command words, this prints every command. Give words to narrow the result. `--search-keys` works with `system list`, `key list`, `policy list` and `tag list`; it fetches filter keys from the API. `--pending` selects the waiting-system keys for `system list`. Organisation options apply only with `--search-keys`.

## Synopsis

```text
enclave-cli commands [<command words>...] [options]
```

## Example

```sh
enclave-cli commands system list --search-keys
```

## Options

| Option | Use |
| --- | --- |
| `--search-keys` | Fetch the list command's filter keys from the API. |
| `--pending` | Use systems waiting for approval. |
| `--org` | Use this organisation by name. |
| `--org-id` | Use this organisation by ID. |

## Output

JSON with command definitions and exit codes.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](README.md#exit-codes).

See the [command index](README.md) for shared input rules.
