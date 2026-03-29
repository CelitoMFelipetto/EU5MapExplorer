$text = Get-Content 'C:\Program Files (x86)\Steam\steamapps\common\Europa Universalis V\game\in_game\map_data\definitions.txt' -Raw
$lines = $text -split "`n"
$subCont = ''
$counts = @{}
foreach ($line in $lines) {
    if ($line -match '^\t(\w+)\s*=\s*\{') { $subCont = $Matches[1]; if (-not $counts[$subCont]) { $counts[$subCont] = 0 } }
    elseif ($line -match '^\t\t\t\t(\w+)\s*=\s*\{' -and $subCont) { $counts[$subCont]++ }
}
$counts.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object {
    '{0,-50} {1,4} areas' -f $_.Key, $_.Value
}
