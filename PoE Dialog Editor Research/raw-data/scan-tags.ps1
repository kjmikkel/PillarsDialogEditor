# Scan PoE2 conversation stringtables for [token] and <token> markup.
# Emits two TSV files: token \t count \t example (truncated) \t example file
param(
    [string]$Dir = "D:\Program Files (x86)\GOG Galaxy\Games\Pillars of Eternity II Deadfire\PillarsOfEternityII_Data\exported\localized\en\text\conversations",
    [string]$OutDir = $PSScriptRoot
)

$square = @{}
$angle  = @{}

$reSquare = [regex]'\[([^\[\]]{1,60})\]'
$reAngle  = [regex]'<([^<>]{1,60})>'

Get-ChildItem $Dir -Recurse -Filter *.stringtable | ForEach-Object {
    $file = $_.FullName
    [xml]$xml = Get-Content $file -Raw
    foreach ($entry in $xml.StringTableFile.Entries.Entry) {
        foreach ($text in @($entry.DefaultText, $entry.FemaleText)) {
            if ([string]::IsNullOrEmpty($text)) { continue }
            foreach ($m in $reSquare.Matches($text)) {
                $key = $m.Groups[1].Value
                if (-not $square.ContainsKey($key)) {
                    $ex = $text; if ($ex.Length -gt 120) { $ex = $ex.Substring(0, 120) + "…" }
                    $square[$key] = [pscustomobject]@{ Count = 0; Example = $ex -replace "`r?`n", " "; File = $_.Name }
                }
                $square[$key].Count++
            }
            foreach ($m in $reAngle.Matches($text)) {
                $key = $m.Groups[1].Value
                if (-not $angle.ContainsKey($key)) {
                    $ex = $text; if ($ex.Length -gt 120) { $ex = $ex.Substring(0, 120) + "…" }
                    $angle[$key] = [pscustomobject]@{ Count = 0; Example = $ex -replace "`r?`n", " "; File = $_.Name }
                }
                $angle[$key].Count++
            }
        }
    }
}

function Dump($map, $path) {
    $map.GetEnumerator() | Sort-Object { $_.Value.Count } -Descending | ForEach-Object {
        "{0}`t{1}`t{2}`t{3}" -f $_.Key, $_.Value.Count, $_.Value.Example, $_.Value.File
    } | Set-Content -Path $path -Encoding UTF8
}

Dump $square (Join-Path $OutDir "tags-square.tsv")
Dump $angle  (Join-Path $OutDir "tags-angle.tsv")
Write-Output ("square distinct: {0}, angle distinct: {1}" -f $square.Count, $angle.Count)
