from PIL import Image, ImageDraw
# LG TV Monitor icon: a TV on the Joystick Keeper / Device Switch dark tile, orange screen, green power light.
S=1024
BG=(0x17,0x19,0x1E,255); LINE=(0x2B,0x2F,0x37,255); OR=(0xFF,0x7A,0x1A,255); ORD=(0xC4,0x55,0x0A,255); GR=(0x38,0xD3,0x9F,255); BGD=(0x0F,0x10,0x13,255)
img=Image.new("RGBA",(S,S),(0,0,0,0)); d=ImageDraw.Draw(img)
d.rounded_rectangle([40,40,S-40,S-40],210,fill=BG,outline=LINE,width=14)
# screen: orange bezel around a dark panel
d.rounded_rectangle([170,230,854,680],48,fill=OR)
d.rounded_rectangle([222,282,802,628],22,fill=BGD)
# stand
d.rounded_rectangle([462,676,562,770],16,fill=ORD)
d.rounded_rectangle([330,760,694,812],26,fill=OR)
# power symbol in the panel, green
cx,cy,r=512,455,96
d.arc([cx-r,cy-r,cx+r,cy+r],start=-55,end=235,fill=GR,width=34)
d.rounded_rectangle([cx-17,cy-r-34,cx+17,cy+8],17,fill=GR)
img.save("icon.png")
sizes=[16,20,24,32,40,48,64,128,256]
img.resize((256,256),Image.LANCZOS).save("../src/LgTvMonitor/LgTvMonitor.ico",sizes=[(z,z) for z in sizes])
prev=Image.new("RGBA",(900,320),BGD); x=30
for z in [256,128,64,48,32,24,16]:
    t=img.resize((z,z),Image.LANCZOS); prev.alpha_composite(t,(x,(320-z)//2)); x+=z+30
prev.save("preview.png")
