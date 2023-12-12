import numpy as np
import plotly.graph_objs as go
from zaber_motion.ascii import Connection
from simple_pyspin import Camera
import sys
import cv2 as cv
from live_profile import spot_tracker

PIXEL_SIZE=2.74
SCALE_FACTOR=0.05

with Connection.open_serial_port("COM4") as connection:
    device_list = connection.detect_devices()
    try:
        device = next(x for x in device_list if "L" in x.name)
        profiler = device.get_axis(1)
    except StopIteration:
        print("No stage available")
        sys.exit()

    def axial_scan(start_pos,end_pos,step):
        with Camera() as cam:
            cam.start()  # Start recording
            if profiler.is_homed():
                profiler.move_absolute(start_pos, "um")
            else:
                sys.exit()
            cam.ExposureAuto = 'Off'
            cam.ExposureTime = 3619  # microseconds
            cam.TriggerSoftware()
            img = cam.get_array()
            #Find and crop to our spot
            crop_x,crop_y,x,y=spot_tracker(img,buffer=-10)
            img = img[crop_x, crop_y]
            img = cv.resize(img, (0, 0), fx=SCALE_FACTOR, fy=SCALE_FACTOR, interpolation=cv.INTER_AREA)
            scan = np.zeros_like(img)
            scan=np.atleast_3d(scan)
            cam.ExposureTime = 38

            for i in np.arange(start_pos,end_pos,-step):
                profiler.move_absolute(i,"um")

                #maxHold for killing time noise
                maxHold=np.zeros_like(scan)
                for b in range(20):
                    cam.TriggerSoftware()
                    img=cam.get_array()
                    cropped_img = img[crop_x, crop_y]
                    cropped_img = cv.medianBlur(cropped_img, 7)
                    spot = cv.resize(cropped_img, (0, 0), fx=SCALE_FACTOR, fy=SCALE_FACTOR,interpolation=cv.INTER_AREA)
                    maxHold=np.append(maxHold,np.atleast_3d(spot),axis=2)
                spot=maxHold.max(axis=2)

                cv.imshow("b",spot)
                cv.imwrite(str(i)+".jpg",spot)
                cv.waitKey(1)
                scan=np.append(scan,np.atleast_3d(spot),axis=2)
            x, y,z=np.shape(scan)

            X,Y,Z=np.meshgrid(np.linspace(0,PIXEL_SIZE*x,x),np.linspace(0,PIXEL_SIZE*y,y),np.linspace(0,1,z), indexing='ij')
            fig=go.Figure(
                data=go.Volume(
                    x=X.flatten(),
                    y=Y.flatten(),
                    z=Z.flatten(),
                    value=scan.flatten(),
                    isomin=25,
                    isomax=255,
                    opacity=0.2,
                    opacityscale=[[0, 0], [100, 0],[200, 0.5], [255, 1]],
                    caps=dict(x_show=False, y_show=False, z_show=False),
                    surface_count=25,
            ))
            fig.write_html("profile_z.html",auto_open=True)
    axial_scan(50000,40000,500)
