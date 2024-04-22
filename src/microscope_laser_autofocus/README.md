# Microscope Laser Autofocus with the WDI ATF sensor and C#

*By Stefan Martin*

## Dependencies

This example code requires a Zaber MKR025B with a MJB25C-785 coupling filter and a WDI PFA-DT-785
laser autofocus sensor. Before using this example, the sensor must be properly aligned according
to the PFA user manual. Currently this autofocus has been tested on wellplates and slide substrates.

## Wise Device SDK

Once you have downloaded the `ATF_LIB` zip file, extract it to a temporary location and copy all the
`atf_lib*.*` files and the `atf_net.dll` file from the `Package\VS2019\bin\x64` directory to the
`thirdparty` directory of this example. They will automatically be copied from there to the location
of the example executable when it is compiled. Similarly, you will have to copy these files to a
location where your own application program can find them.


## Building and running the example

Open the `MicroscopeLaserAF.sln` file with Visual Studio 2022 (the free Community edition will work).

Look at the constants at the top of the `Program.cs` source file and make sure they match your
hardware configuration. You will most likely have to change the values of the `ZABER_PORT` and
`ATF_PORT_OR_IP` constants. Save the file after making any changes.

On the first run, it is necessary to uncomment the Objective.MeasureSlope() line and
manually focus the microscope using Zaber Launcher microscope app or a joystick.
Once slope measurement has been completed for each objective you wish to use,
you may comment out this line.

Always ensure you have Zaber Launcher running before starting this example code or Micro-Manager.
Zaber Launcher provides port sharing capability that enables both to work at once.

Finally, compile the example by pressing `CTRL-SHIFT-B` and run it by pressing `F5`.


## Troubleshooting Tips

- If you get an error saying that the focus axis could not be found, make sure you are using the correct
serial port for Zaber devices. If that does not solve the problem, use the
[Zaber Launcher](https://software.zaber.com/zaber-launcher/download) Microscope App to find the microscope
components; it will label them in a way that helps the example code find them.
