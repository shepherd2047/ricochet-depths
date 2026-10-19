import sys, numpy as np
from PIL import Image
sys.path.insert(0, '.')
from sprites import all_sprites
S = all_sprites()
items = list(S.items())
cols, cell = 12, 36
rows = (len(items)+cols-1)//cols
sheet = np.zeros((rows*cell, cols*cell, 4), np.uint8); sheet[:] = (38,41,55,255)
for i,(k,a) in enumerate(items):
    y, x = (i//cols)*cell+2, (i%cols)*cell+2
    h, w = a.shape[:2]; h=min(h,cell-4); w=min(w,cell-4)
    sub = a[:h,:w]; m = sub[:,:,3]>0
    sheet[y:y+h, x:x+w][m] = sub[m]
Image.fromarray(sheet).resize((sheet.shape[1]*4, sheet.shape[0]*4), Image.NEAREST).save(sys.argv[1])
print(len(items))
