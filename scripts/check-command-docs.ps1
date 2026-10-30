$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$catalogue = dotnet run --no-build --project (Join-Path $root 'src/Enclave.Cli') -- commands | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Could not read the CLI command catalogue.' }
$indexPath = Join-Path $root 'docs/commands/README.md'
if (-not (Test-Path $indexPath)) { throw 'Missing docs/commands/README.md.' }
$index = Get-Content $indexPath -Raw
$readme = Get-Content (Join-Path $root 'README.md') -Raw
if ($readme -notmatch '\]\(docs/commands/README\.md\)') { throw 'README.md does not link to the command index.' }
foreach ($entry in $catalogue.commands) {
    $parts = $entry.command.Split(' ')
    $relative = ($parts -join '/') + '.md'
    $path = Join-Path $root ('docs/commands/' + $relative)
    if (-not (Test-Path $path)) { throw "Missing command page: $relative" }
    $page = Get-Content $path -Raw
    if ($page -notmatch [regex]::Escape(('`enclave-cli ' + $entry.command + '`'))) { throw "Command page does not name its command: $relative" }
    if ($index -notmatch [regex]::Escape($relative)) { throw "Command index does not link to: $relative" }
    if ($page -notmatch '(?m)^## Synopsis\r?$' -or $page -notmatch '(?m)^## Example\r?$' -or $page -notmatch '(?m)^## Output\r?$') { throw "Command page lacks a reference section: $relative" }
    foreach ($match in [regex]::Matches($page, '\]\(([^)#]+\.md)(?:#[^)]+)?\)')) {
        if (-not (Test-Path (Join-Path (Split-Path -Parent $path) $match.Groups[1].Value))) { throw "Broken link in $relative`: $($match.Groups[1].Value)" }
    }
}
Write-Output ("Checked {0} command pages and README links." -f $catalogue.commands.Count)
