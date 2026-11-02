import os
HERE = os.path.dirname(os.path.abspath(__file__))
from PIL import Image, ImageDraw, ImageFilter
BOLT=[(0.62,0.04),(0.26,0.56),(0.47,0.56),(0.38,0.96),(0.76,0.40),(0.54,0.40),(0.80,0.04)]
def render(S=1024):
    img=Image.new("RGBA",(S,S),(0,0,0,0))
    # rounded dark tile
    m=Image.new("L",(S,S),0); ImageDraw.Draw(m).rounded_rectangle([0,0,S-1,S-1],radius=int(S*0.22),fill=255)
    bg=Image.new("RGBA",(S,S))
    px=bg.load()
    for y in range(S):
        for x in range(S):
            d=((x-S*0.7)**2+(y-S*0.2)**2)**0.5/S
            k=max(0,1-d*1.6)
            px[x,y]=(int(3+44*k),int(5+28*k),int(9+6*k),255)
    img.paste(bg,(0,0),m)
    pts=[(S*(0.10+0.80*x),S*(0.08+0.84*y)) for x,y in BOLT]
    glow=Image.new("RGBA",(S,S),(0,0,0,0))
    ImageDraw.Draw(glow).polygon(pts,fill=(242,163,58,255))
    for rad,al in ((S*0.06,0.9),(S*0.025,0.8)):
        g=glow.filter(ImageFilter.GaussianBlur(rad)); a=g.split()[3].point(lambda v:int(v*al)); g.putalpha(a)
        img=Image.alpha_composite(img,g)
    # gradient fill
    fill=Image.new("RGBA",(S,S))
    fp=fill.load()
    for y in range(S):
        t=y/S
        c=(int(255-(255-200)*t),int(224-(224-122)*t),int(160-(160-30)*t),255)
        for x in range(S): fp[x,y]=c
    bm=Image.new("L",(S,S),0); ImageDraw.Draw(bm).polygon(pts,fill=255)
    img.paste(fill,(0,0),bm)
    # rim
    ImageDraw.Draw(img).line(pts+[pts[0]],fill=(255,244,220,255),width=max(2,S//170),joint="curve")
    # tile border
    ring=Image.new("RGBA",(S,S),(0,0,0,0)); ImageDraw.Draw(ring).rounded_rectangle([2,2,S-3,S-3],radius=int(S*0.22),outline=(242,163,58,120),width=max(2,S//128))
    img=Image.alpha_composite(img,ring)
    return img
im=render()
im.save(os.path.join(HERE, "cuidatubateria.ico"),sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
im.resize((256,256),Image.LANCZOS).save(os.path.join(HERE, "preview.png"))
