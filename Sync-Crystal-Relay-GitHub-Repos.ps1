param(
    [string]$PublicRepoPath = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GitHub\crystal-relay-public')
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exportScriptPath = Join-Path $root 'tools\github\Export-Crystal-Relay-Public.ps1'

& $exportScriptPath -PublicRepoPath $PublicRepoPath
