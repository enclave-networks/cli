# `enclave-cli logout`

This deletes the saved credentials file. The token remains valid until it is revoked in the portal. Other tools that use `Enclave.Sdk.Api` read the same file.

## Synopsis

```text
enclave-cli logout
```

## Example

```sh
enclave-cli logout
```

## Output

JSON with the credentials path and a `deleted` boolean.

Successful commands write JSON to standard output. Errors write JSON to standard error and use a nonzero [exit code](README.md#exit-codes).

See the [command index](README.md) for shared input rules.
