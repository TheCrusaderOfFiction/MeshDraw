"""Offline raster filters. Independent equivalents, not ibis Paint's proprietary code.

Input/output are straight RGBA PNG; calculations that resample use premultiplied
alpha. The desktop applies its selection mask after processing, at full size.
"""
import argparse
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image
from scipy import ndimage as nd
from scipy.spatial import cKDTree


def process(image, key, params=None, scale=1.0):
    p = params or {}
    def v(name, default):
        return float(p.get(name, default))
    def color(name, default):
        value = p.get(name, default).lstrip('#')
        return np.array([int(value[i:i+2], 16) for i in (0, 2, 4)], np.float32) / 255
    src = np.asarray(image.convert('RGBA'), dtype=np.float32) / 255
    h, w = src.shape[:2]
    rgb, a = src[..., :3].copy(), src[..., 3].copy()
    yy, xx = np.indices((h, w), dtype=np.float32)
    cx, cy = v('center_x', .5)*(w-1), v('center_y', .5)*(h-1)
    dx, dy = xx-cx, yy-cy
    radius = max(.1, v('radius', 8)*scale)
    size = max(2, v('size', 16)*scale)
    strength = v('strength', 1)
    angle = np.deg2rad(v('angle', 45))
    rng = np.random.default_rng(int(v('seed', 12)))
    grey = rgb @ np.array([.2126, .7152, .0722], np.float32)
    c1, c2 = color('color', '#306EBB'), color('color2', '#F7CF79')
    pm = src.copy(); pm[..., :3] *= a[..., None]
    def unpack(data):
        alpha = np.clip(data[..., 3], 0, 1)
        return np.clip(data[..., :3]/np.maximum(alpha[..., None], 1e-7), 0, 1), alpha
    def remap(data, x, y, border=cv2.BORDER_CONSTANT):
        return cv2.remap(data, np.asarray(x, np.float32), np.asarray(y, np.float32), cv2.INTER_LINEAR, borderMode=border)
    def blur(data, sigma):
        return nd.gaussian_filter(data, (sigma, sigma, 0) if data.ndim == 3 else sigma, mode='constant')
    def over(base_rgb, base_a, top_rgb, top_a):
        alpha = top_a + base_a*(1-top_a)
        out = top_rgb*top_a[..., None] + base_rgb*base_a[..., None]*(1-top_a[..., None])
        return out/np.maximum(alpha[..., None], 1e-7), alpha
    def edges(data=grey):
        return np.clip(np.hypot(nd.sobel(data, 0)/8, nd.sobel(data, 1)/8)*v('detail', 3), 0, 1)
    def pixel_cells(mode):
        if mode in ('crystalize', 'pointillize', 'stained_glass'):
            gx, gy = np.meshgrid(np.arange(size/2, w, size), np.arange(size/2, h, size))
            if gx.size == 0: gx, gy = np.array([[w/2]]), np.array([[h/2]])
            points = np.column_stack((gx.ravel(), gy.ravel()))
            points += rng.uniform(-size*.4, size*.4, points.shape)
            dist, labels = cKDTree(points).query(np.column_stack((xx.ravel(), yy.ravel())))
            return labels.reshape(h, w), dist.reshape(h, w)
        row = np.floor(yy/(size*.866 if 'hex' in mode else size)).astype(np.int32)
        shift = (row % 2)*size/2 if 'hex' in mode else 0
        col = np.floor((xx-shift)/size).astype(np.int32)
        if 'hex' in mode:
            # Nearest hex center; inspect adjacent rows as well as the same row.
            best = np.full((h,w), np.inf, np.float32); labels = np.zeros((h,w), np.int32)
            for off in (-1,0,1):
                rr = row+off; cc = np.rint((xx-(rr%2)*size/2-size/2)/size).astype(np.int32)
                d = (xx-(cc*size+(rr%2)*size/2+size/2))**2+(yy-(rr*size*.866+size*.433))**2
                chosen=d<best; best[chosen]=d[chosen]; labels[chosen]=((rr+2)*(int(w/size)+8)+cc+2)[chosen]
            return labels, np.sqrt(best)
        labels = row*(int(w/size)+2)+col
        dist = np.hypot((xx%size)-size/2, (yy%size)-size/2)
        if mode == 'triangle': labels = labels*2 + ((xx%size)+(yy%size)>size)
        return labels, dist
    def mosaic(mode):
        labels, distance = pixel_cells(mode)
        _, inv = np.unique(labels, return_inverse=True); inv=inv.reshape(h,w)
        count = np.bincount(inv.ravel())
        means = np.stack([np.bincount(inv.ravel(), weights=pm[...,i].ravel())/count for i in range(4)], axis=1)
        return means[inv].astype(np.float32), labels, distance

    if key == 'brightness_contrast': rgb = (rgb-.5)*(1+v('contrast', .2))+.5+v('brightness', .1)
    elif key == 'tone_curve':
        rgb = np.interp(rgb, [0,.25,.5,.75,1], [v('black',0),v('shadows',.2),v('midtones',.5),v('highlights',.85),v('white',1)])
    elif key == 'hsl':
        hls=cv2.cvtColor(rgb, cv2.COLOR_RGB2HLS); hls[...,0]=(hls[...,0]+v('hue',30))%360
        hls[...,2]=np.clip(hls[...,2]*(1+v('saturation',.2)),0,1)
        hls[...,1]=np.clip(hls[...,1]+v('lightness',0),0,1)
        rgb=cv2.cvtColor(hls,cv2.COLOR_HLS2RGB)
    elif key == 'color_balance': rgb += np.array([v('red',.15),v('green',0),v('blue',-.1)])
    elif key in ('extract_lines','edges_handwriting','find_edges'):
        white = grey*a+(1-a)
        if key=='extract_lines': a=a*(1-white)**max(.1,v('gamma',1));rgb[:]=c1
        elif key=='edges_handwriting': a=a*edges(white);rgb[:]=c1
        else: rgb=np.repeat(edges(white)[...,None],3,axis=2)
    elif key=='drawing_color': rgb[:]=c1
    elif key=='mono_color': rgb=grey[...,None]*c1
    elif key=='grayscale': rgb=np.repeat(grey[...,None],3,axis=2)
    elif key=='black_white': rgb=np.repeat((grey>=v('threshold',.5))[...,None],3,axis=2).astype(np.float32)
    elif key=='posterize':
        levels=max(2,round(v('levels',5)));rgb=np.round(rgb*(levels-1))/(levels-1)
    elif key=='invert': rgb=1-rgb
    elif key=='gradient_map': rgb=c1[None,None,:]*(1-grey[...,None])+c2*grey[...,None]
    elif key=='levels':
        low,high=v('black',.05),v('white',.95)
        if high<=low: raise ValueError('White input must be greater than black input.')
        rgb=np.clip((rgb-low)/(high-low),0,1)**(1/max(.1,v('gamma',1)))
    elif key=='replace_color':
        match=np.linalg.norm(rgb-c2,axis=2)<=v('tolerance',.25);rgb[match]=c1
    elif key=='gaussian': rgb,a=unpack(blur(pm,radius))
    elif key=='lens':
        r=max(1,round(radius)); ky,kx=np.indices((2*r+1,2*r+1));qx=kx-r;qy=ky-r
        # Hexagonal bokeh aperture, rotated by the direction control.
        u=qx*np.cos(angle)+qy*np.sin(angle);vv=-qx*np.sin(angle)+qy*np.cos(angle)
        kernel=((np.abs(u)<=r*.866)&(np.abs(vv)+np.abs(u)/np.sqrt(3)<=r)).astype(np.float32)
        kernel/=max(1,kernel.sum());data=cv2.filter2D(pm,-1,kernel,borderType=cv2.BORDER_CONSTANT)
        rgb,a=unpack(data);rgb*=v('brightness',1.1)
    elif key in ('zoom_blur','motion_blur','spin_blur','god_rays'):
        data=np.zeros_like(pm)
        samples=24
        for t in np.linspace(-.5,.5,samples):
            if key in ('zoom_blur','god_rays'): x=cx+dx*(1+t*radius/max(w,h)*4);y=cy+dy*(1+t*radius/max(w,h)*4)
            elif key=='motion_blur': x=xx+t*radius*2*np.cos(angle);y=yy+t*radius*2*np.sin(angle)
            else:
                theta=t*np.deg2rad(v('angle',20));x=cx+dx*np.cos(theta)-dy*np.sin(theta);y=cy+dx*np.sin(theta)+dy*np.cos(theta)
            data+=remap(pm,x,y)/samples
        if key=='god_rays': rgb,a=over(rgb,a,c1,np.clip(data[...,3]*strength*.65,0,1))
        else: rgb,a=unpack(data)
    elif key in ('mosaic','square','hex','triangle','crystalize','pointillize','dots_hex','dots_square','stained_glass'):
        data,labels,distance=mosaic('hex' if key=='dots_hex' else 'square' if key in ('mosaic','dots_square') else key)
        rgb,a=unpack(data)
        if key in ('pointillize','dots_hex','dots_square'): a*=distance<=size*v('dot_size',.4)
        if key=='stained_glass':
            boundary=(labels!=np.roll(labels,1,0))|(labels!=np.roll(labels,1,1))
            boundary=nd.maximum_filter(boundary,size=max(1,round(v('border',2)*scale)))
            rgb[boundary]=c1;a[boundary]=1
    elif key=='unsharp': rgb=np.clip(rgb+(rgb-blur(rgb,radius))*strength,0,1)
    elif key=='frosted': rgb,a=unpack(remap(pm,xx+rng.uniform(-radius,radius,(h,w)),yy+rng.uniform(-radius,radius,(h,w))))
    elif key in ('stroke_both','stroke_outer','inner_glow','outer_glow','wet_edge','bevel_inner','bevel_outer','waterdrop','drop_shadow','extrude','satin'):
        solid=a>.1
        inside=nd.distance_transform_edt(np.pad(solid,1))[1:-1,1:-1];outside=nd.distance_transform_edt(~solid)
        if not solid.any(): inside[:]=0;outside[:]=1e9
        if key in ('stroke_outer','stroke_both'):
            ta=(outside<=radius).astype(np.float32)*(1-a)
            if key=='stroke_both':ta=np.maximum(ta,(inside<=radius)*a)
            rgb,a=over(rgb,a,c1,ta)
        elif key in ('inner_glow','wet_edge'):
            edge=np.exp(-inside/max(.5,radius))*a
            if key=='wet_edge': rgb*=1-edge[...,None]*.65*strength
            else: rgb=rgb*(1-edge[...,None]*strength)+c1*edge[...,None]*strength
        elif key=='outer_glow':
            ta=blur(a,radius)*(1-a)*strength;rgb,a=over(c1,np.clip(ta,0,1),rgb,a)
        elif key in ('drop_shadow','extrude'):
            shifted=np.zeros_like(a)
            for t in ([1] if key=='drop_shadow' else np.linspace(0,1,24)):
                shifted=np.maximum(shifted,remap(a,xx-t*radius*np.cos(angle),yy-t*radius*np.sin(angle)))
            if key=='drop_shadow':shifted=blur(shifted,max(.1,v('softness',4)*scale))
            rgb,a=over(c1,shifted*np.clip(strength,0,1),rgb,a)
        elif key=='satin':
            shadow=np.abs(remap(a,xx-radius,yy-radius)-remap(a,xx+radius,yy+radius));rgb*=1-shadow[...,None]*strength*.8
        else:
            height=np.minimum(inside/radius,1)
            if key=='waterdrop':height=np.sqrt(np.clip(1-(1-height)**2,0,1))
            gy,gx=np.gradient(blur(height,max(.5,radius/6)));light=(gx*np.cos(angle)+gy*np.sin(angle))*radius
            rgb=np.clip(rgb+light[...,None]*strength*.7,0,1)
            if key=='bevel_outer':rgb,a=over(c1,(outside<=radius)*(1-a)*.7,rgb,a)
    elif key in ('emboss','relief','relief_hq','chrome'):
        height=blur(grey,max(.1,radius/4)) if key!='emboss' else grey
        gy,gx=np.gradient(height);light=(gx*np.cos(angle)+gy*np.sin(angle))*strength*8
        if key=='emboss':rgb=np.repeat(np.clip(.5+light,0,1)[...,None],3,2)
        elif key=='chrome':rgb=np.repeat((.5+.5*np.sin((grey+light)*v('frequency',12)))[...,None],3,2)
        elif key=='relief_hq':
            nx=-gx*strength*20;ny=-gy*strength*20
            norm=np.sqrt(nx*nx+ny*ny+1)
            diffuse=np.clip((nx*np.cos(angle)+ny*np.sin(angle)+1)/norm/np.sqrt(2),0,1)
            specular=np.clip(1/norm,0,1)**24
            rgb=np.clip(rgb*(.35+diffuse[...,None])+specular[...,None]*.15,0,1)
        else:rgb=np.clip(rgb+light[...,None],0,1)
    elif key in ('parallel_gradient','concentric_gradient','radial_gradient','radial_lines','speed_lines','clouds','qr','table','rain'):
        if key=='parallel_gradient': t=np.clip(.5+(dx*np.cos(angle)+dy*np.sin(angle))/max(w,h),0,1)
        elif key=='concentric_gradient': t=np.clip(np.hypot(dx,dy)/(min(w,h)/2),0,1)
        elif key in ('radial_gradient','radial_lines'):
            t=((np.arctan2(dy,dx)-angle)/(2*np.pi))%1
            if key=='radial_lines':t=(((t*max(3,v('count',40)))%1)<v('width',.2)).astype(np.float32)
        elif key=='speed_lines':t=(((xx*np.cos(angle)+yy*np.sin(angle))/size)%1<v('width',.15)).astype(np.float32)
        elif key=='clouds':
            t=np.zeros((h,w),np.float32)
            for octave in range(5):
                gh=max(2,int(h/(size*4)*2**octave));gw=max(2,int(w/(size*4)*2**octave))
                t+=cv2.resize(rng.random((gh,gw),dtype=np.float32),(w,h),interpolation=cv2.INTER_CUBIC)/2**octave
            t=(t-t.min())/max(1e-6,float(t.max()-t.min()))
        elif key=='qr':
            text=str(p.get('text','MeshDraw'))
            if not text or len(text.encode('utf-8'))>1000:raise ValueError('QR text must contain 1–1000 UTF-8 bytes.')
            encoded=cv2.QRCodeEncoder_create().encode(text)
            encoded=cv2.copyMakeBorder(encoded,4,4,4,4,cv2.BORDER_CONSTANT,value=255)
            n=min(w,h); modules=encoded.shape[0];factor=n//modules
            if factor<1:raise ValueError('Canvas is too small for this QR code. Shorten the text.')
            result=np.ones((h,w),np.float32)
            code=cv2.resize(encoded,(modules*factor,modules*factor),interpolation=cv2.INTER_NEAREST)/255
            ox=(w-code.shape[1])//2;oy=(h-code.shape[0])//2;result[oy:oy+code.shape[0],ox:ox+code.shape[1]]=code
            rgb=np.repeat(result[...,None],3,2);a[:]=1;t=None
        elif key=='table':
            cols=max(1,round(v('columns',4)));rows=max(1,round(v('rows',4)));line=max(1,v('border',2)*scale)
            t=((xx%(w/cols)<line)|(yy%(h/rows)<line)|(xx>=w-line)|(yy>=h-line)).astype(np.float32)
        else:
            drops=(rng.random((h,w))<v('density',.006)).astype(np.float32)
            t=np.zeros((h,w),np.float32)
            for d in np.linspace(0,radius*3,20):t=np.maximum(t,remap(drops,xx-d*np.cos(angle),yy-d*np.sin(angle)))
            rgb,a=over(rgb,a,c1,t*np.clip(strength,0,1));t=None
        if t is not None:
            if key in ('radial_lines','speed_lines','table'):rgb,a=over(rgb,a,c1,t*np.clip(strength,0,1))
            else:rgb=c1*(1-t[...,None])+c2*t[...,None];a[:]=1
    elif key in ('watercolor','anime','oil_paint'):
        smoothed=cv2.bilateralFilter(rgb,9,min(.9,.1+radius/60),max(1,radius))
        levels=max(2,round(v('levels',8)));paint=np.round(smoothed*(levels-1))/(levels-1)
        if key=='watercolor':paint=paint*(1-rng.random((h,w,1),dtype=np.float32)*v('grain',.08));paint=paint*.85+blur(paint,1)*.15
        elif key=='anime':paint*=1-edges(smoothed@np.array([.2126,.7152,.0722]))[...,None]*.4
        else:
            gy,gx=np.gradient(blur(grey,max(1,radius/2)));paint+=((gx+gy)*strength*2)[...,None]
        rgb=rgb*(1-np.clip(strength,0,1))+paint*np.clip(strength,0,1)
    elif key=='background_remove':
        # Flood only edge-connected pixels close to the chosen background color.
        bg=color('color','#FFFFFF');close=(np.linalg.norm(rgb-bg,axis=2)<v('tolerance',.2))|(a<.01)
        seed=np.zeros((h,w),bool);seed[0]=close[0];seed[-1]=close[-1];seed[:,0]=close[:,0];seed[:,-1]=close[:,-1]
        removed=nd.binary_propagation(seed,mask=close);a[removed]=0
    elif key=='manga':
        spacing=max(2,size);tone=(np.hypot(xx%spacing-spacing/2,yy%spacing-spacing/2)/spacing)<np.sqrt(1-grey)*.5
        ink=np.maximum(tone.astype(np.float32),edges());rgb=np.repeat((1-ink)[...,None],3,2)
    elif key in ('color_shift','rgb_shift'):
        distance=radius
        if key=='rgb_shift':
            data=pm.copy();data[...,0]=remap(pm[...,0],xx-distance,yy);data[...,2]=remap(pm[...,2],xx+distance,yy)
            data[...,3]=np.maximum.reduce([a,remap(a,xx-distance,yy),remap(a,xx+distance,yy)]);rgb,a=unpack(data)
        else:
            shifted=remap(pm,xx-radius*np.cos(angle),yy-radius*np.sin(angle));sr,sa=unpack(shifted)
            rgb,a=over(rgb,a,sr*c1,sa*.5)
    elif key=='glitch':
        offsets=rng.integers(-round(radius),round(radius)+1,max(1,int(np.ceil(h/size))))
        data=remap(pm,xx+offsets[np.minimum(len(offsets)-1,(yy/size).astype(int))],yy);rgb,a=unpack(data)
        rgb[...,0]=np.roll(rgb[...,0],max(1,round(radius/2)),axis=1)
    elif key=='noise': rgb+=rng.normal(0,v('amount',.15),(h,w,3)).astype(np.float32)
    elif key=='retro':
        data,_,_=mosaic('square');rgb,a=unpack(data);levels=max(2,round(v('levels',4)));rgb=np.round(rgb*(levels-1))/(levels-1)
    elif key in ('bloom','cross'):
        bright=np.clip((grey-v('threshold',.6))/max(.01,1-v('threshold',.6)),0,1)*a
        if key=='bloom':glow=blur(bright,radius)
        else:
            glow=np.zeros((h,w),np.float32)
            for t in np.linspace(-radius*4,radius*4,32):
                weight=np.exp(-abs(t)/max(.5,radius*2))/16
                glow+=remap(bright,xx+t*np.cos(angle),yy+t*np.sin(angle))*weight
                glow+=remap(bright,xx-t*np.sin(angle),yy+t*np.cos(angle))*weight
        rgb,a=over(rgb,a,c1,np.clip(glow*strength,0,1))
    elif key=='sheer':
        shape=int(v('shape',0));u=xx%size-size/2;vv=yy%size-size/2;thin=max(1,scale)
        if shape==0: pattern=(abs(u)<thin)|(abs(vv)<thin)
        elif shape==1: pattern=abs(u)<thin
        elif shape==2: pattern=abs(np.hypot(u,vv)-size*.3)<thin
        else:pattern=(abs(abs(u)-size*.3)<thin)|(abs(abs(vv)-size*.3)<thin)
        rgb=rgb*(1-pattern[...,None]*.35*strength)+c1*pattern[...,None]*.35*strength
    elif key in ('expansion','fisheye','sphere','wave','ripple','twirl','polar'):
        dist=np.hypot(dx,dy);rn=dist/max(1,min(w,h)/2);theta=np.arctan2(dy,dx)
        if key=='expansion': x=cx+dx/max(.1,v('zoom',1.25));y=cy+dy/max(.1,v('zoom',1.25))
        elif key in ('fisheye','sphere'):
            if key=='fisheye':factor=1+v('distortion',.5)*rn**2
            else:factor=np.where(rn<1,1-v('distortion',.5)*np.sqrt(np.clip(1-rn**2,0,1)),1)
            x=cx+dx*factor;y=cy+dy*factor
        elif key=='wave':x=xx+radius*np.sin(yy/size*2*np.pi+angle);y=yy+radius*np.sin(xx/size*2*np.pi+angle)*v('vertical',0)
        elif key=='ripple':
            offset=radius*np.sin(dist/size*2*np.pi+angle);x=xx+dx/np.maximum(dist,1)*offset;y=yy+dy/np.maximum(dist,1)*offset
        elif key=='twirl':
            theta+=np.deg2rad(v('angle',90))*np.maximum(0,1-rn);x=cx+dist*np.cos(theta);y=cy+dist*np.sin(theta)
        else:x=(theta/(2*np.pi)+.5)*(w-1);y=np.clip(rn,0,1)*(h-1)
        rgb,a=unpack(remap(pm,x,y))
    elif key=='blur_frame':
        inset=np.minimum.reduce([xx,yy,w-1-xx,h-1-yy]);t=np.clip((v('border',24)*scale-inset)/max(1,v('border',24)*scale),0,1)
        data=pm*(1-t[...,None])+blur(pm,radius)*t[...,None];rgb,a=unpack(data)
    else: raise ValueError(f'Unknown filter: {key}')
    out=np.concatenate([np.clip(rgb,0,1),np.clip(a,0,1)[...,None]],axis=2)
    quantized=np.rint(out*255).astype(np.uint8)
    quantized[quantized[...,3]==0,:3]=0
    return Image.fromarray(quantized,'RGBA')


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--job',required=True);args=parser.parse_args()
    job=json.loads(Path(args.job).read_text(encoding='utf-8-sig'))
    image=Image.open(job['input']);result=process(image,job['filter'],job.get('params'),job.get('scale',1))
    result.save(job['output']);print('Filter complete',flush=True)


if __name__=='__main__': main()
