"""Filter browser definitions. The JSON beside this file is consumed by WPF."""
import json
from pathlib import Path


def slider(key, label, low, high, value, step=.01):
    return dict(key=key,label=label,min=low,max=high,value=value,step=step,type='number')


R=slider('radius','Radius / distance (px)',0,100,8,1)
S=slider('size','Cell / pattern size (px)',2,150,16,1)
A=slider('angle','Angle (degrees)',-180,180,45,1)
ST=slider('strength','Strength',0,2,1)
SEED=slider('seed','Pattern seed',0,9999,12,1)
CX=slider('center_x','Center X',0,1,.5)
CY=slider('center_y','Center Y',0,1,.5)
C=dict(key='color',label='Color / foreground',type='color',text='#306EBB')
C2=dict(key='color2',label='Second color / source color',type='color',text='#F7CF79')
LEVEL=slider('levels','Color levels',2,32,5,1)
DETAIL=slider('detail','Edge detail',.1,10,3)
CATALOG=[]


def add(category, key, name, *controls, note=''):
    CATALOG.append(dict(category=category,key=key,name=name,controls=list(controls),note=note))


G='Adjust Color'
add(G,'brightness_contrast','Brightness & Contrast',slider('brightness','Brightness',-1,1,.1),slider('contrast','Contrast',-1,3,.2))
add(G,'tone_curve','Tone Curve',*[slider(k,l,0,1,d) for k,l,d in [('black','Output at input 0%',0),('shadows','Output at input 25%',.2),('midtones','Output at input 50%',.5),('highlights','Output at input 75%',.85),('white','Output at input 100%',1)]],note='Five adjustable curve points, with linear interpolation.')
add(G,'hsl','Hue Saturation Lightness',slider('hue','Hue (degrees)',-180,180,30,1),slider('saturation','Saturation',-1,2,.2),slider('lightness','Lightness',-1,1,0))
add(G,'color_balance','Color Balance',*[slider(c,c.title(),-1,1,d) for c,d in [('red',.15),('green',0),('blue',-.1)]])
add(G,'extract_lines','Extract Line Drawing',C,slider('gamma','Line density',.1,4,1))
add(G,'edges_handwriting','Find Edges (Handwriting)',C,DETAIL)
add(G,'find_edges','Find Edges',DETAIL)
add(G,'drawing_color','Change Drawing Color',C)
add(G,'mono_color','Mono Color',C)
add(G,'grayscale','Grayscale')
add(G,'black_white','Black & White',slider('threshold','Threshold',0,1,.5))
add(G,'posterize','Posterize',LEVEL)
add(G,'invert','Invert Color')
add(G,'gradient_map','Gradation Map',C,C2)
add(G,'levels','Levels Adjustment',slider('black','Black input',0,.99,.05),slider('white','White input',.01,1,.95),slider('gamma','Gamma',.1,4,1))
add(G,'replace_color','Replace Color',C,C2,slider('tolerance','Color tolerance',0,1,.25))
G='Blur'
add(G,'gaussian','Gaussian Blur',R)
add(G,'zoom_blur','Zooming Blur',R,CX,CY)
add(G,'motion_blur','Moving Blur',R,A)
add(G,'spin_blur','Spin Blur',A,CX,CY)
add(G,'lens','Lens Blur',R,A,slider('brightness','Brightness',.1,3,1.1))
add(G,'mosaic','Mosaic',S)
add(G,'unsharp','Unsharp Mask',R,ST)
add(G,'frosted','Frosted Glass',R,SEED)
G='Style'
add(G,'stroke_both','Stroke (Both)',R,C)
add(G,'stained_glass','Stained Glass',S,C,slider('border','Lead width (px)',1,12,2,1),SEED)
add(G,'wet_edge','Wet Edge',R,ST)
add(G,'inner_glow','Glow (Inner)',R,ST,C)
add(G,'bevel_inner','Bevel (Inner)',R,ST,A)
add(G,'bevel_outer','Bevel (Outer)',R,ST,A,C)
add(G,'emboss','Emboss',ST,A)
add(G,'relief','Relief',R,ST,A)
add(G,'relief_hq','Relief HQ',R,ST,A,note='Local shaded relief. Does not use ibis Paint’s smart depth service.')
add(G,'waterdrop','Waterdrop (Rounded)',R,ST,A)
add(G,'stroke_outer','Stroke (Outer)',R,C)
add(G,'outer_glow','Glow (Outer)',R,ST,C)
add(G,'satin','Satin',R,ST)
add(G,'drop_shadow','Drop Shadow',R,A,C,slider('softness','Softness (px)',0,50,4,1),slider('strength','Opacity',0,1,.75))
add(G,'extrude','Extrude',R,A,C,slider('strength','Opacity',0,1,1))
add(G,'god_rays','God Rays',R,CX,CY,ST,C)
G='Draw'
add(G,'parallel_gradient','Parallel Gradation',A,CX,CY,C,C2)
add(G,'concentric_gradient','Concentric Gradation',CX,CY,C,C2)
add(G,'radial_gradient','Radial Line Gradation',A,CX,CY,C,C2)
add(G,'radial_lines','Radial Line',A,CX,CY,C,slider('count','Line count',3,200,40,1),slider('width','Line width fraction',.01,.9,.2))
add(G,'speed_lines','Speed Line',A,S,C,slider('width','Line width fraction',.01,.9,.15))
add(G,'clouds','Clouds',S,SEED,C,C2)
add(G,'qr','QR Code',dict(key='text',label='QR text or URL',type='text',text='MeshDraw'),note='Black and white QR code with a quiet border. Keep effect opacity at 100% for scanning.')
G='Smart · local alternatives'
add(G,'watercolor','Watercolor Filter',R,slider('strength','Strength',0,1,1),LEVEL,SEED,slider('grain','Paper grain',0,.3,.08),note='Offline watercolor approximation; does not use ibis Paint’s smart service.')
add(G,'background_remove','Background Removal',dict(C,text='#FFFFFF',label='Background color'),slider('tolerance','Color tolerance',.01,1,.2),note='Offline removal of matching background connected to canvas edges. Not AI subject segmentation; complex backgrounds may need your selection brush.')
G='Artistic'
add(G,'anime','Anime Background',R,slider('strength','Strength',0,1,1),LEVEL,DETAIL)
add(G,'manga','Manga Background',S,DETAIL)
add(G,'color_shift','Chromatic Aberration · Color Shift',R,A,C)
add(G,'rgb_shift','Chromatic Aberration · RGB Shift',R)
add(G,'glitch','Glitch',R,S,SEED)
add(G,'noise','Noise',slider('amount','Noise amount',0,.5,.15),SEED)
add(G,'retro','Retro Game',S,LEVEL)
add(G,'chrome','Chrome',R,ST,A,slider('frequency','Metal bands',1,30,12))
add(G,'bloom','Bloom',R,ST,C,slider('threshold','Highlight threshold',0,.99,.6))
add(G,'cross','Cross Filter',R,ST,A,C,slider('threshold','Highlight threshold',0,.99,.6))
add(G,'sheer','Sheer',S,ST,C,slider('shape','Shape: 0 cross · 1 line · 2 circle · 3 square',0,3,0,1))
add(G,'oil_paint','Oil Paint',R,slider('strength','Strength',0,1,1),LEVEL,note='Offline smoothing, color quantization, and brush relief.')
G='Pixelate'
add(G,'crystalize','Pixelate Crystalize',S,SEED)
add(G,'hex','Hexagonal Pixelate',S)
add(G,'square','Square Pixelate',S)
add(G,'triangle','Triangular Pixelate',S)
add(G,'pointillize','Pointillize',S,SEED,slider('dot_size','Dot radius fraction',.05,.7,.4))
add(G,'dots_hex','Dots (Hexagonal)',S,slider('dot_size','Dot radius fraction',.05,.5,.4))
add(G,'dots_square','Dots (Square)',S,slider('dot_size','Dot radius fraction',.05,.5,.4))
G='Transform'
add(G,'expansion','Expansion',slider('zoom','Scale',.1,4,1.25),CX,CY)
add(G,'fisheye','Fisheye Lens',slider('distortion','Distortion',-.8,2,.5),CX,CY)
add(G,'sphere','Sphere Lens',slider('distortion','Distortion',-.8,.95,.5),CX,CY)
add(G,'wave','Wave',R,S,A,slider('vertical','Vertical wave',0,1,0))
add(G,'ripple','Ripple',R,S,A,CX,CY)
add(G,'twirl','Twirl',dict(A,value=90,min=-720,max=720),CX,CY)
add(G,'polar','Polar Coordinates',CX,CY)
G='Frame'
add(G,'table','Table',slider('rows','Rows',1,40,4,1),slider('columns','Columns',1,40,4,1),slider('border','Line width (px)',1,30,2,1),C)
add(G,'blur_frame','Blur Frame',R,slider('border','Frame width (px)',1,200,24,1))
add('Movie · still effects','rain','Rain',R,A,C,SEED,slider('density','Rain density',.001,.05,.006,.001),slider('strength','Opacity',0,1,.7),note='A single rain frame for this drawing. Change Pattern seed for a different frame; MeshDraw does not export filter animations.')

if __name__=='__main__':
    target=Path(__file__).with_name('filter_catalog.json')
    target.write_text(json.dumps(CATALOG,indent=2,ensure_ascii=False),encoding='utf-8')
    print(f'{len(CATALOG)} filters written')
