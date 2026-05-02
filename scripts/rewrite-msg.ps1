Param()

# Read full original commit message from stdin
$orig = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($orig)) { exit 0 }

$firstLine = ($orig -split "\r?\n")[0].Trim()

# Determine type via simple heuristics
$type = 'chore'
if ($orig -match '(?i)\b(BREAKING CHANGE|BREAKING|!)\b') { $type = 'feat' }
elseif ($orig -match '(?i)\b(fix|bug)\b') { $type = 'fix' }
elseif ($orig -match '(?i)\b(add|feat|feature)\b') { $type = 'feat' }
elseif ($orig -match '(?i)\b(remove|delete|rm)\b') { $type = 'chore' }
elseif ($orig -match '(?i)\b(readme|docs)\b') { $type = 'docs' }

# Assemble new message (avoid PowerShell interpolation pitfalls)
$new = $type + ': ' + $firstLine + "`n`nOriginal commit message:`n" + $orig
Write-Output $new
