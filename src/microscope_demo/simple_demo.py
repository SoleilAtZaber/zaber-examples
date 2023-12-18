from zaber_motion.microscopy import Microscope, MicroscopeConfig
from zaber_motion.ascii import Connection
from zaber_motion import AxisAddress, Measurement, Units

MSR_default_config = MicroscopeConfig(
    illuminator=2,  # X-LCA address
    focus_axis=AxisAddress(3, 1),  # X-LDA address, axis 1
    filter_changer=4,  # X-FCR address
    objective_changer=5,  # X-MOR address
    x_axis=AxisAddress(6, 1),  # X-ASR address, axis 1
    y_axis=AxisAddress(6, 2),  # X-ASR address, axis 2
)

MVR_default_config = MicroscopeConfig(
    illuminator=2,  # X-LCA address
    focus_axis=AxisAddress(3, 1),  # X-LDA address, axis 1
    filter_changer=4,  # X-FCR address
    x_axis=AxisAddress(5, 1),  # X-ASR address, axis 1
    y_axis=AxisAddress(5, 2),  # X-ASR address, axis 2
)

OBJECTIVES = {
    1: {
        "magnification": 20,
        "offset_um": 5},
    2: {
        "magnification": 5,
        "offset_um": -5},
    3: {
        "magnification": 10,
        "offset_um": 10},
    4: {
        "magnification": 1,
        "offset_um": 100},
}

with Connection.open_serial_port("COM4") as connection:
    print(connection.detect_devices())

    Nucleus = Microscope(connection, MSR_default_config)
    Nucleus.initialize()
    print("Homing complete")
    objective_changer = Nucleus.objective_changer
    filter_cube = Nucleus.filter_changer

    # Set focus position after an objective change. Focus offsets are measured from this datum.
    # This can be set to the focused position so that the next objective is moved directly into focus
    # NOTE: For NON-PARFOCAL objectives we recommend 15mm to keep a safe offset distance from the sample
    objective_changer.set_focus_datum(15, unit="mm")

    plate=Nucleus.plate

    print("Synchronized move to start position")
    plate.move_absolute(Measurement(50, 'mm'), Measurement(50, 'mm'))

    print("Focus move")
    Nucleus.focus_axis.move_relative(100,"um")

    print("Simple snaking scan")
    stepover = 20 / OBJECTIVES[objective_changer.get_current_objective()]["magnification"]
    for x in range(10):
        Nucleus.x_axis.move_relative(stepover, "mm")
        for y in range(10):
            # Alternating directions for snake pattern
            Nucleus.y_axis.move_relative(stepover * pow(-1, x),"mm")

    print("Retract the objective")
    objective_changer.release()

    print("Change filters")
    filter_cube.change(2)

    print("Show off range of motion of XY")
    plate.move_max()
    plate.move_min()

    print("change objectives with an offset")
    objective_changer.change(3, focus_offset=Measurement(OBJECTIVES[3]['offset_um'], unit="um"))
