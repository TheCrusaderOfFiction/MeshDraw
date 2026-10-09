param([string]$Python = "python")
$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
Set-Location -LiteralPath $projectDirectory
function Check-Result { if ($LASTEXITCODE -ne 0) { throw "Local setup failed. See the error above." } }
if (-not (Test-Path -LiteralPath 'local-ai/Hunyuan3D-2/hy3dgen')) {
    git clone --depth 1 https://github.com/Tencent-Hunyuan/Hunyuan3D-2.git local-ai/Hunyuan3D-2
    Check-Result
}
& $Python -m venv --without-pip local-ai/venv
Check-Result
& $Python -m pip --python local-ai/venv/Scripts/python.exe install torch==2.11.0+cu128 torchvision==0.26.0+cu128 --index-url https://download.pytorch.org/whl/cu128
Check-Result
& $Python -m pip --python local-ai/venv/Scripts/python.exe install -r backend/requirements.txt
Check-Result
& local-ai/venv/Scripts/python.exe backend/download_models.py
Check-Result
& local-ai/venv/Scripts/python.exe -c "import torch; assert torch.cuda.is_available(), 'CUDA is unavailable'; print(torch.cuda.get_device_name(0))"
Check-Result
Write-Host 'Local setup complete. Open MeshDraw and choose Generate locally.'
