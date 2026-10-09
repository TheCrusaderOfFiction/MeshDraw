"""One-time download; generation itself operates offline."""
import os
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
os.environ['HF_HOME'] = str(ROOT / 'local-ai' / 'cache')
from huggingface_hub import snapshot_download
snapshot_download('tencent/Hunyuan3D-2mv',
    allow_patterns=['hunyuan3d-dit-v2-mv/config.yaml', 'hunyuan3d-dit-v2-mv/model.fp16.safetensors'],
    local_dir=str(ROOT / 'local-ai' / 'models' / 'Hunyuan3D-2mv'))
print('Local multiview model downloaded.')
