param(
    [string]$CommitMsgFile = $args[0]
)

$msg = Get-Content -Raw -Path $CommitMsgFile -ErrorAction Stop | Select-String -Pattern '.*' -AllMatches | ForEach-Object { $_.Matches[0].Value } | Select-Object -First 1

$pattern = '^(feat|fix|docs|style|refactor|perf|test|chore|build|ci|revert)(\([a-z0-9\-]+\))?(!)?: .+'

if ($msg -match $pattern) { exit 0 }

Write-Error 'ERROR: Commit message does not follow Conventional Commits.'
Write-Error 'Expected: <type>(scope?): <description>'
Write-Error 'See: https://www.conventionalcommits.org/'
exit 1
