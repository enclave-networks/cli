#!/bin/sh
# Runs a published enclave-cli once and checks it reports the version CI built it with. This proves
# the published binary starts on the platform it was built for. The unit tests run the CLI as an
# ordinary .NET assembly, so they cannot catch a failure that exists only in the published
# single-file executable.
#
# POSIX sh, because it also runs inside Alpine containers, which have no bash.
#
# Usage: smoke-test.sh <path-to-enclave-cli> <expected-version>
set -eu

exe=$1
expected=$2

actual=$("$exe" --version)
echo "enclave-cli --version: $actual"

case "$actual" in
  "$expected+"*) ;;
  *)
    echo "::error::expected a version starting '$expected+', got '$actual'"
    exit 1
    ;;
esac
