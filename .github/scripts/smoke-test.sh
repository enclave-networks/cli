#!/bin/sh
# Unpacks a release archive into an empty directory and runs the enclave-cli it holds: it checks
# the executable reports the version CI built it with, describes its commands as JSON, and reports
# a missing token as the CLI contract says. This proves the archive that ships holds an executable
# that starts and runs commands on the platform it was built for. The unit tests run the CLI as an
# ordinary .NET assembly, so they cannot catch a failure that exists only in the published
# single-file executable or in the archive. No command here makes a network call.
#
# POSIX sh, because it also runs inside Alpine containers, which have no bash.
#
# Usage: smoke-test.sh <archive> <expected-version>
set -eu

archive=$1
expected=$2

fail() {
  echo "::error::$1"
  exit 1
}

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir "$work/unpacked" "$work/home"

# The zip holds the Windows build and each tar.gz another one (.github/workflows/ci.yml, the
# Package steps). On Windows this runs in Git for Windows' sh, and Git for Windows includes unzip
# in every install but MinGit (git-for-windows/build-extra, make-file-list.sh, the required
# packages, commit 1f042be).
case "$archive" in
  *.zip)
    unzip -q "$archive" -d "$work/unpacked"
    exe=$work/unpacked/enclave-cli.exe
    ;;
  *.tar.gz)
    tar -xzf "$archive" -C "$work/unpacked"
    exe=$work/unpacked/enclave-cli
    ;;
  *)
    fail "$archive is not a .zip or a .tar.gz"
    ;;
esac

# The licence requires its notice in "all copies or substantial portions of the Software"
# (LICENSE, MIT), so the archive carries it.
[ -f "$work/unpacked/LICENSE" ] || fail "$archive holds no LICENSE"

# Each run gets an empty home directory and none of the variables the CLI reads its token,
# organisation and partner from, ENCLAVE_TOKEN, ENCLAVE_ORG, ENCLAVE_ORG_ID and ENCLAVE_PARTNER_ID
# (src/Enclave.Cli/Context/ApiAccess.cs, OrganisationResolver.cs and PartnerResolver.cs), so what
# it prints depends only on the archive and no token on the machine reaches the API. Every variable
# whose name starts ENCLAVE_ in any case is removed, because environment variable names are not
# case-sensitive on Windows
# (https://learn.microsoft.com/dotnet/api/system.environment.getenvironmentvariable).
for name in $(env | sed -n 's/^\([Ee][Nn][Cc][Ll][Aa][Vv][Ee]_[A-Za-z0-9_]*\)=.*/\1/p'); do
  unset "$name"
done
HOME=$work/home
export HOME

# On Linux and macOS the CLI's home directory is $HOME (dotnet/runtime release/10.0,
# src/libraries/System.Private.CoreLib/src/System/IO/PersistedFiles.Unix.cs, GetHomeDirectory). On
# Windows it is the account's profile folder, from SHGetKnownFolderPath with FOLDERID_Profile
# (src/libraries/System.Private.CoreLib/src/System/Environment.Win32.cs, GetFolderPathCore), which
# neither HOME nor USERPROFILE changes: with .NET 10.0.1 on Windows 11 and both set elsewhere, it
# stayed C:\Users\<account>. So on Windows the script stops when that folder holds a credentials
# file, whose token org list would send to the API. cygpath -F 40 prints that folder: 40 is
# CSIDL_PROFILE (0x0028 in the Windows SDK's Shlobj_core.h), the CSIDL for FOLDERID_Profile
# (https://learn.microsoft.com/windows/win32/shell/csidl).
case "$archive" in
  *.zip)
    profile=$(cygpath -w -F 40)
    credentials=$(cygpath -u "$profile")/.enclave/credentials.json
    # The CLI names the file by its Windows path, and JSON escapes each backslash as \\.
    named=$(printf '%s\\.enclave\\credentials.json' "$profile" | sed 's/\\/\\\\/g')
    ;;
  *)
    credentials=$HOME/.enclave/credentials.json
    named=$credentials
    ;;
esac
[ ! -e "$credentials" ] || fail "$credentials exists, and enclave-cli org list would send its token to the API"

run() {
  status=0
  "$exe" "$@" >"$work/stdout" 2>"$work/stderr" || status=$?
}

actual=$("$exe" --version)
echo "enclave-cli --version: $actual"

case "$actual" in
  "$expected+"*) ;;
  *)
    echo "::error::expected a version starting '$expected+', got '$actual'"
    exit 1
    ;;
esac

# `commands` makes no call (proposed-cli-surface.md "Calls per command") and prints one JSON
# document. The stock alpine:3.22 and almalinux:8 images have no JSON parser on the PATH, neither
# jq nor python (checked in October 2026), so the check is on the form the CLI writes,
# System.Text.Json's indented output (src/Enclave.Cli/Core/CliJson.cs, Output). The first byte is
# the opening brace, so there is no byte order mark; the last line is the closing brace, so the
# document is complete; and the top-level commands and errors arrays hold org list and
# token_missing, which the next check relies on. Indented output ends its lines with
# Environment.NewLine, \r\n on Windows
# (https://learn.microsoft.com/dotnet/api/system.text.json.jsonserializeroptions.newline), so \r
# is dropped first.
run commands
[ "$status" -eq 0 ] || fail "enclave-cli commands exited $status: $(cat "$work/stderr")"
[ ! -s "$work/stderr" ] || fail "enclave-cli commands wrote to stderr: $(cat "$work/stderr")"
tr -d '\r' <"$work/stdout" >"$work/commands.json"
[ "$(head -c 1 "$work/commands.json")" = "{" ] || fail "enclave-cli commands output does not start with {"
[ "$(tail -n 1 "$work/commands.json")" = "}" ] || fail "enclave-cli commands output does not end with }"
grep -q '^  "commands": \[$' "$work/commands.json" || fail "enclave-cli commands output has no commands array"
grep -q '^  "errors": \[$' "$work/commands.json" || fail "enclave-cli commands output has no errors array"
grep -q '"command": "org list",' "$work/commands.json" || fail "enclave-cli commands does not describe org list"
grep -q '"code": "token_missing",' "$work/commands.json" || fail "enclave-cli commands does not list token_missing"
echo "enclave-cli commands: exit 0, a JSON document on stdout"

# With no token the CLI exits 3 before it makes any call: it checks the arguments, then the token,
# then the organisation, and only then calls the API (proposed-cli-surface.md "Errors and exit
# codes"). It writes nothing on stdout and one compact JSON error on stderr
# (src/Enclave.Cli/Core/CliOutput.cs, WriteErrorAsync), which names the credentials file it looked
# for, so the error also shows the CLI looked in the home directory set above.
run org list
[ "$status" -eq 3 ] || fail "enclave-cli org list exited $status, expected 3: $(cat "$work/stderr")"
[ ! -s "$work/stdout" ] || fail "enclave-cli org list wrote to stdout: $(cat "$work/stdout")"
[ "$(wc -l <"$work/stderr" | tr -d ' ')" = 1 ] || fail "enclave-cli org list wrote other than one line to stderr: $(cat "$work/stderr")"
error=$(tr -d '\r' <"$work/stderr")
case "$error" in
  '{"error":{"code":"token_missing",'*'}}') ;;
  *) fail "expected a token_missing error from enclave-cli org list, got: $error" ;;
esac
case "$error" in
  *"$named"*) ;;
  *) fail "expected the token_missing error to name $credentials, got: $error" ;;
esac
echo "enclave-cli org list: exit 3, token_missing on stderr"
