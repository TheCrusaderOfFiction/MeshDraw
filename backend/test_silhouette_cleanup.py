import tempfile
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
from generate import silhouette
root=Path(__file__).resolve().parent.parent/'outputs'/'silhouette-cleanup-checks';root.mkdir(parents=True,exist_ok=True)
with tempfile.TemporaryDirectory(dir=root) as folder:
    path=Path(folder)/'front.png'
    image=Image.new('RGB',(256,256),'white');draw=ImageDraw.Draw(image)
    draw.rectangle((70,60,170,190),outline='black',width=4)
    draw.rectangle((185,80,205,100),outline='black',width=3)
    image.save(path);_,clean=silhouette(path)
    draw.ellipse((10,10,15,15),fill='black');draw.line((15,230,240,230),fill=(190,190,190),width=3)
    image.save(path);_,marked=silhouette(path)
    assert np.array_equal(clean,marked) and marked[90,195]
    image=Image.new('RGB',(256,256),'white');ImageDraw.Draw(image).line((30,30,200,200),fill='black',width=5);image.save(path)
    try:silhouette(path);raise AssertionError('Open outline accepted')
    except ValueError:pass
print('PASS: stray dots/open strokes ignored, detached closed parts preserved, open-only drawing rejected.')

# Reproduce the reported six-view failure when its saved inputs are available.
source=root.parent/"20261009-145655-d7c8c4"
if source.exists():
    from silhouette_fit import reference_volume
    masks={n:silhouette(source/f"{n}.png")[1] for n in ("front","back","left","right","top","bottom")}
    volume,_,_=reference_volume(masks,192)
    assert volume.sum()>32
    print("PASS: reported drawing has usable shared volume after cleanup.")
