# `enclave-cli login`

Run it in a terminal. It asks for a personal access token, checks it, then asks you to choose a default organisation, and saves both in `~/.enclave/credentials.json`. Press Enter at the token question to keep the token already saved. Programs set `ENCLAVE_CLI_PAT` and don't run `login`.

## Synopsis

```text
enclave-cli login
```

## Example

```sh
enclave-cli login
```

## Output

A JSON list with `kind`, `items` and `total`.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](README.md#exit-codes).

See the [command index](README.md) for shared input rules.
