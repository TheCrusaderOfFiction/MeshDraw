"""Acceptance checks for offline filters: pixels, alpha, QR and full-size limits."""
import time
import unittest
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw
from filters import process
from filter_catalog import CATALOG


class FilterChecks(unittest.TestCase):
    def sample(self, size=128):
        y,x=np.indices((size,size));data=np.zeros((size,size,4),np.uint8)
        data[...,0]=x*255//size;data[...,1]=y*255//size;data[...,2]=60
        data[...,3]=np.where((x>size//5)&(x<size*4//5)&(y>size//5)&(y<size*4//5),200,0)
        return Image.fromarray(data,'RGBA')

    def test_catalog_and_every_filter(self):
        self.assertEqual(len({f['key'] for f in CATALOG}),len(CATALOG))
        source=self.sample();outdir=Path('outputs/filter-engine-checks');outdir.mkdir(parents=True,exist_ok=True)
        thumbs=[]
        for item in CATALOG:
            with self.subTest(filter=item['name']):
                params={c['key']:c.get('value') if c['type']=='number' else c['text'] for c in item['controls']}
                result=process(source,item['key'],params)
                self.assertEqual(result.size,source.size)
                arr=np.array(result);self.assertEqual(arr.shape,(128,128,4))
                self.assertTrue(np.all(arr[arr[...,3]==0,:3]==0),'Hidden color remains in transparent pixels')
                self.assertFalse(np.array_equal(arr,np.array(source)), 'Default filter has no effect')
                tile=Image.new('RGB',(164,168),'#e1e5ea');tile.paste(result,(18,0),result)
                ImageDraw.Draw(tile).text((4,132),item['name'][:24],fill='black');thumbs.append(tile)
        sheet=Image.new('RGB',(164*6,168*((len(thumbs)+5)//6)),'white')
        for i,tile in enumerate(thumbs):sheet.paste(tile,((i%6)*164,(i//6)*168))
        sheet.save(outdir/'all-filters.png')

    def test_color_math_and_alpha(self):
        source=Image.new('RGBA',(16,16),(30,100,210,127));arr=np.array(process(source,'invert'))
        np.testing.assert_array_equal(arr[8,8],[225,155,45,127])
        grey=np.array(process(source,'grayscale'));self.assertTrue(np.all(grey[...,0]==grey[...,1]));self.assertTrue(np.all(grey[...,1]==grey[...,2]))
        self.assertTrue(np.all(grey[...,3]==127))
        light=np.array(process(Image.new('RGBA',(16,16),'red'),'hsl',{'hue':0,'saturation':0,'lightness':.25}))
        np.testing.assert_array_equal(light[8,8],[255,128,128,255])

    def test_blur_has_no_black_fringe_and_styles_expand(self):
        image=Image.new('RGBA',(64,64));ImageDraw.Draw(image).rectangle((24,24,40,40),fill=(255,0,0,255))
        blurred=np.array(process(image,'gaussian',{'radius':4}));live=blurred[...,3]>0
        self.assertTrue(np.all(blurred[live,0]==255));self.assertTrue(np.all(blurred[live,1:3]==0))
        stroke=np.array(process(image,'stroke_outer',{'radius':5}));self.assertGreater(np.count_nonzero(stroke[...,3]),np.count_nonzero(np.array(image)[...,3]))

    def test_background_preserves_enclosed_foreground(self):
        image=Image.new('RGBA',(64,64),'white');draw=ImageDraw.Draw(image);draw.rectangle((15,15,48,48),fill='red');draw.rectangle((25,25,38,38),fill='white')
        out=np.array(process(image,'background_remove',{'color':'#FFFFFF','tolerance':.1}))
        self.assertEqual(out[0,0,3],0);self.assertEqual(out[32,32,3],255);self.assertEqual(out[17,17,3],255)

    def test_qr_scans(self):
        text='https://example.com/meshdraw'
        out=np.array(process(Image.new('RGBA',(512,512)),'qr',{'text':text}))
        decoded,_,_=cv2.QRCodeDetector().detectAndDecode(out[...,:3]);self.assertEqual(decoded,text)

    def test_determinism_and_errors(self):
        source=self.sample();a=np.array(process(source,'clouds',{'seed':45}));b=np.array(process(source,'clouds',{'seed':45}))
        np.testing.assert_array_equal(a,b)
        self.assertFalse(np.array_equal(a,np.array(process(source,'clouds',{'seed':46}))))
        with self.assertRaises(ValueError):process(source,'does_not_exist')
        with self.assertRaises(ValueError):process(source,'levels',{'black':.8,'white':.1})
        with self.assertRaises(ValueError):process(source,'qr',{'text':''})

    def test_large_canvas(self):
        image=self.sample(2048);measurements={}
        for key in ('gaussian','lens','hex','crystalize','twirl','watercolor'):
            begin=time.monotonic();result=process(image,key);measurements[key]=round(time.monotonic()-begin,2)
            self.assertEqual(result.size,(2048,2048))
            self.assertLess(measurements[key],30,f'{key} exceeds laptop response budget')
        Path('outputs/filter-engine-checks/performance.json').write_text(__import__('json').dumps(measurements,indent=2),encoding='utf-8')


if __name__=='__main__':unittest.main()
