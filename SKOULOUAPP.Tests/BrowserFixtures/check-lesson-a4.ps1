$ErrorActionPreference = 'Stop'
$chrome = @('C:\Program Files\Google\Chrome\Application\chrome.exe', 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) { throw 'Chrome oder Edge fehlt.' }
$uri = ([Uri](Resolve-Path (Join-Path $PSScriptRoot 'lesson-a4.html')).Path).AbsoluteUri
$profile = Join-Path $env:TEMP ("skoulou-a4-profile-" + [Guid]::NewGuid().ToString('N'))
$output = Join-Path $env:TEMP ("skoulou-a4-dom-" + [Guid]::NewGuid().ToString('N') + '.txt')
$errors = Join-Path $env:TEMP ("skoulou-a4-err-" + [Guid]::NewGuid().ToString('N') + '.txt')
foreach ($kind in @('paragraph', 'list', 'image', 'all', 'modal')) {
	$process = Start-Process -FilePath $chrome -ArgumentList @('--headless', '--disable-gpu', '--no-sandbox', '--disable-web-security', '--allow-file-access-from-files', "--user-data-dir=$profile", '--virtual-time-budget=18000', '--dump-dom', "${uri}?kind=$kind") -Wait -PassThru -RedirectStandardOutput $output -RedirectStandardError $errors
	if ($process.ExitCode -ne 0) { throw "Chrome fehlgeschlagen: $(Get-Content $errors -Raw)" }
	$line = [regex]::Match((Get-Content $output -Raw), '<pre id="result">([^<]*)</pre>').Groups[1].Value
	if (-not $line -or $line.StartsWith('ERROR:')) { throw "Browsertest $kind fehlgeschlagen: $line" }
	$result = $line | ConvertFrom-Json
	if ($result.savedPages -ne $result.sheetCount -or $result.overflowCount -ne 0 -or $result.readerOverflow.Count -ne 0) { throw "A4-Abweichung ($kind): $line" }
	if ($kind -eq 'modal' -and ($result.modal.scrollHeight -le $result.modal.clientHeight -or $result.modal.scrollTop -le 0 -or $result.modal.secondPageTop -lt 0 -or $result.modal.secondPageTop -ge $result.modal.clientHeight -or $result.modal.editorOverflow -ne 'visible' -or $result.modal.sheetColor -ne 'rgb(255, 255, 255)')) { throw "A4-Seiten im Modal nicht sichtbar: $line" }
	Write-Output "$kind`: $($result.savedPages) Seiten, kein Ueberlauf"
}
$pdf = Join-Path $env:TEMP ("skoulou-a4-check-" + [Guid]::NewGuid().ToString('N') + '.pdf')
try {
	$process = Start-Process -FilePath $chrome -ArgumentList @('--headless', '--disable-gpu', '--no-sandbox', '--disable-web-security', '--allow-file-access-from-files', "--user-data-dir=$profile", '--no-pdf-header-footer', '--virtual-time-budget=18000', "--print-to-pdf=$pdf", "${uri}?kind=all") -Wait -PassThru -RedirectStandardOutput $output -RedirectStandardError $errors
	if ($process.ExitCode -ne 0) { throw "Chrome-PDF fehlgeschlagen: $(Get-Content $errors -Raw)" }
	if (-not (Test-Path $pdf)) { throw 'Chrome hat kein PDF erzeugt.' }
	$raw = [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($pdf))
	$count = [regex]::Matches($raw, '/Type\s*/Page\b').Count
	if ($count -ne $result.savedPages) { throw "PDF: $count Seiten, gespeichert: $($result.savedPages)" }
	Write-Output "PDF: $count Seiten, identisch mit Editor und Reader"
} finally { foreach ($path in @($pdf, $output, $errors)) { if (Test-Path $path) { Remove-Item $path -Force } }; if (Test-Path $profile) { Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue } }
