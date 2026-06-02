$ErrorActionPreference = "Stop"

$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$Backend = Join-Path $Root "backend"
$VenvPython = Join-Path $Root ".venv311\Scripts\python.exe"
$Python = if (Test-Path $VenvPython) { $VenvPython } else { "python" }

$env:HOST = "0.0.0.0"
$env:PORT = "5000"
$env:FLASK_DEBUG = "0"
$env:OCR_PROVIDER = "tesseract"
$env:TRANSLATION_PROVIDER = "mymemory"
$env:MYMEMORY_SOURCE_LANGUAGE = "en"
$env:OCR_MIN_CONFIDENCE = "0.18"
$env:OCR_PREPROCESS_ENABLED = "1"
$env:OCR_UPSCALE_LONG_SIDE = "3200"
$env:OCR_MAX_UPSCALE = "4.0"
$env:OCR_GRAYSCALE = "1"
$env:OCR_AUTOCONTRAST = "1"
$env:OCR_CONTRAST = "1.35"
$env:OCR_SHARPNESS = "1.45"
$env:OCR_UNSHARP_MASK = "1"
$env:TESSERACT_LANG = "eng"

$Tesseract = Get-Command tesseract -ErrorAction SilentlyContinue
if ($Tesseract) {
    $env:TESSERACT_CMD = $Tesseract.Source
}

Write-Host "Demo backend: real OCR=tesseract, real translation=mymemory"
Write-Host "Listening on http://0.0.0.0:5000"
Write-Host "Unity Android scene is set to http://192.168.1.7:5000"

Push-Location $Backend
try {
    & $Python app.py
}
finally {
    Pop-Location
}
