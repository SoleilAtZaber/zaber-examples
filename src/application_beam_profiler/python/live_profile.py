import numpy as np
from simple_pyspin import Camera
#import plotly.graph_objs as go
from matplotlib import cm
import cv2 as cv
import matplotlib.pyplot as plt

fig = plt.figure()
plt.ion()
ax = fig.add_subplot(111, projection='3d')
X, Y = np.meshgrid(np.linspace(0,1,10),np.linspace(0,1,10))
plot=[ax.plot_surface(X,Y,np.zeros((10,10)),cmap=cm.jet)]
def spot_tracker(img, buffer: int=0):
    median = cv.medianBlur(img, 3)

    # do canny edge detection
    canny = cv.Canny(median, 100, 200)

    # get canny points
    # numpy points are (y,x)
    points = np.argwhere(canny > 0)

    # get min enclosing circle
    center, radius = cv.minEnclosingCircle(points)
    #(minVal, maxVal, minLoc, maxLoc) = cv.minMaxLoc(blur)
    x = int(center[0])
    y = int(center[1])
    radius = int(radius)+buffer
    max_x,max_y=np.shape(img)
    if radius>0:
        cropx=slice(max(x-radius,0),min(x+radius,max_x))
        cropy=slice(max(y-radius,0),min(y+radius,max_y))
        cropped_img = img[cropx, cropy]

        # Crop image using Numpy slicing
        #cv.circle(img,(x,y),radius,0,20)
        #spot = cv.resize(img, (0, 0), fx=0.2, fy=0.2)
        #cv.imshow("tst",cropped_img)
        #cv.waitKey(10)
        y,x=np.shape(cropped_img)
        X, Y = np.meshgrid(np.linspace(0,1,x),np.linspace(0,1,y))
        plot[0].remove()
        plot[0]=ax.plot_surface(X,Y,cropped_img,cmap=cm.jet)
        plt.show()
        plt.pause(.01)
        return cropx,cropy,x,y
    else: return False

def main():
    with Camera() as cam:
        cam.start()  # Start recording
        while (1):
            img=cam.get_array()
            spot_tracker(img)

if __name__=="__main__":
    main()
