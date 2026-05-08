param(
    [string]$PublicRepoPath = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GitHub\crystal-relay-public')
)

$ErrorActionPreference = "Stop"

$exportScript = Join-Path $PSScriptRoot 'tools\github\Export-Crystal-Relay-Public.ps1'
& $exportScript -PublicRepoPath $PublicRepoPath
