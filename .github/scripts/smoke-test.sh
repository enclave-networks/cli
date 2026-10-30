#!/bin/sh
# Runs the enclave-cli a release archive holds. The unit tests run the CLI as an ordinary .NET
# assembly, so they cannot catch a failure that exists only in the published single-file
# executable or in the archive. No command here makes a network call.
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

# The zip is the Windows build (ci.yml, the Package steps). There this runs in Git for Windows'
# sh, and every Git for Windows install but MinGit includes unzip (git-for-windows/build-extra,
# make-file-list.sh, commit 1f042be).
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

# An empty home directory and no ENCLAVE_ variables, so the output depends only on the archive and
# no token on the machine reaches the API. In any case, because environment variable names are not
# case-sensitive on Windows
# (https://learn.microsoft.com/dotnet/api/system.environment.getenvironmentvariable).
for name in $(env | sed -n 's/^\([Ee][Nn][Cc][Ll][Aa][Vv][Ee]_[A-Za-z0-9_]*\)=.*/\1/p'); do
  unset "$name"
done
HOME=$work/home
export HOME

# On Windows the CLI's home directory is the profile folder, FOLDERID_Profile, which neither HOME
# nor USERPROFILE changes (dotnet/runtime release/10.0, Environment.Win32.cs GetFolderPathCore), so
# the script stops when that folder holds a credentials file, whose token org list would send to
# the API. cygpath -F 40 prints that folder: 40 is CSIDL_PROFILE
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

# The stock alpine:3.22 and almalinux:8 images have neither jq nor python, so this checks the
# indented form the CLI writes (src/Enclave.Cli/Core/CliJson.cs, Output): { as the first byte, so
# no byte order mark, and } as the last line, so the document is complete. Indented output ends
# lines with Environment.NewLine, \r\n on Windows
# (https://learn.microsoft.com/dotnet/api/system.text.json.jsonserializeroptions.newline).
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

# With no token the CLI exits 3 before any call (spec/cli-surface.md "Errors and exit codes"). The
# error names the credentials file, which shows the CLI looked in the home directory worked out
# above.
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
