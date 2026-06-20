<#
.SYNOPSIS
  Downloads a new version of the bundled embedding model.
  After running, update manifest.json with the new sha256 printed below,
  run dotnet test to verify similarity thresholds, then submit a PR.

.PARAMETER ModelUrl
  URL to the replacement .onnx file. Defaults to the current model source.

.PARAMETER VocabUrl
  URL to the replacement vocab.txt. Defaults to current source.
#>
param(
    [string]$ModelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model_quint8_avx2.onnx",
    [string]$VocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt"
)

$modelDir = "$PSScriptRoot\..\models\all-MiniLM-L6-v2"
$onnxPath = "$modelDir\model_quint8_avx2.onnx"
$vocabPath = "$modelDir\vocab.txt"

Write-Host "Downloading vocab.txt..."
Invoke-WebRequest -Uri $VocabUrl -OutFile $vocabPath -UseBasicParsing

Write-Host "Downloading model (~22 MB)..."
Invoke-WebRequest -Uri $ModelUrl -OutFile $onnxPath -UseBasicParsing

$hash = (Get-FileHash $onnxPath -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Download complete."
Write-Host "SHA256: $hash"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Update models/all-MiniLM-L6-v2/manifest.json with the sha256 above"
Write-Host "  2. Run: dotnet test  (similarity thresholds must pass)"
Write-Host "  3. Submit a PR — the manifest diff documents the upgrade"
