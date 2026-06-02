$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$VenvPython = Join-Path $Root ".venv311\Scripts\python.exe"
$Python = if (Test-Path $VenvPython) { $VenvPython } else { "python" }
$BackendUrl = "http://127.0.0.1:5000"

$Tesseract = Get-Command tesseract -ErrorAction SilentlyContinue
if (-not $Tesseract) {
    Write-Error "Tesseract is not in PATH. Install it before the recording demo."
}

Write-Host "Checking backend health..."
$Health = Invoke-RestMethod "$BackendUrl/health" -TimeoutSec 5
$Health | ConvertTo-Json -Depth 5

Write-Host "Posting sample slide through real OCR and real translation..."
& $Python (Join-Path $Root "scripts\post_sample_frame.py") `
    --image (Join-Path $Root "samples\slides\slide_01.png") `
    --url "$BackendUrl/pipeline/frame" `
    --real `
    --ocr-provider tesseract `
    --translation-provider mymemory `
    --timeout 60
