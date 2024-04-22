using Zaber.Motion;
using System;
using System.Timers;
using WDI;

using Zaber.Motion.Ascii;
using Zaber.Motion.Microscopy;
using System.Threading;

//NOTE: Zaber Launcher MUST BE OPEN before launching either this script or MicroManager. This allows multiple programs to access the serial port concurrently

namespace MicroscopeLaserAF
{
    internal class Program
    {
        // Change these constants to match your hardware configuration.
        static readonly string ZABER_PORT = "COM4";
        static readonly string ATF_PORT_OR_IP = "169.254.64.162";
        static readonly int ATF_SPEED = 27;
        static readonly int MINIMUM_FOCUS = 18; //This should be set close to the lowest extent of the sample

        static void Main()
        {
            Program test = new Program();
            test.TestAutofocus();
        }

        /// <summary>
        /// Initializes the microscope and the autofocus sensor and then demo of both single and continuous autofocus modes
        /// </summary>
        public void TestAutofocus()
        {
            //This object stores the autofocus parameters for the current objective.
            var curObjective = new Objective(0);

            using (var connection = Connection.OpenSerialPort(ZABER_PORT))
            {
                connection.EnableAlerts();
                connection.DetectDevices();

                var _MVR = Microscope.Find(connection);
                if (_MVR.FocusAxis == null)
                {
                    Console.WriteLine("Error: Could not identify microscope focus axis. Please use the Zaber Launcher microscope app to configure your devices.");
                    return;
                }

                //Initialize the microscope
                _MVR.Initialize();

                _focusAxis = _MVR.FocusAxis;
                _objectiveChanger = _MVR.ObjectiveChanger;

                // Open TCP/IP connection to PFA sensor
                ATF.ATF_OpenConnection(ATF_PORT_OR_IP, ATF_SPEED);

                // Setup the laser
                ATF.ATF_DisableAutoOff();
                ATF.ATF_EnableLaser();
                ATF.ATF_LaserTrackOn();

                //Load Objective 1, either unsing the X-MOR or manually
                //ChangeObjectives(1, curObjective);
                curObjective.SetAll(1);

                //Uncomment this line when first setting up your objective. This allows you to compute the relationship between the sensor and real world units.
                //curObjective.MeasureSlope(_focusAxis);

                // Assuming that the user has done "make0" manually, autofocus the microscope
                AFOnce(curObjective);

                Thread.Sleep(1000);

                // Run continous autofocus
                PeriodicAF(curObjective);

                // Cleanup
                ATF.ATF_CloseConnection();
            }
        }

        /// <summary>
        /// Used with the X-MOR4 objective changer to switch to a new objective and load settings from the autofocus
        /// </summary>
        public bool ChangeObjectives(int objectiveNumber, Objective current)
        {
            _objectiveChanger.Change(objectiveNumber);

            if (ATF.ATF_WriteObjNum(objectiveNumber) == 0)
            {
                current.SetAll(objectiveNumber);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Autofocuses on a timer every 5 seconds until the user cancels it.
        /// </summary>
        public void PeriodicAF(Objective obj)
        {
            int UpdateInterval = 5000; // 5 seconds, modify this depending on how often the autofocus routine should run
            var focusTimer = new System.Timers.Timer();
            focusTimer.Elapsed += delegate
            {
                Tracking(obj);
            };
            focusTimer.Interval = UpdateInterval; 
            focusTimer.Start();
            Console.WriteLine("Autofocusing every " + UpdateInterval/1000 + "s, press Enter to exit.");
            Console.ReadLine();
        }

        /// <summary>
        /// Event handler for the periodic autofocus Elapsed event.
        /// </summary>
        private static void Tracking(Objective obj)
    {
        var ecode = ATF.ATF_ReadPosition(out var fpos);
        if (ecode == 0)
        {
                if (Math.Abs(fpos) > 0.8 * obj.SensorRange || fpos==0)
                {
                    Console.WriteLine("Focus error is excessive, running search");
                    AFOnce(obj);
                }
                else {
                    int iter = 0;
                    while (Math.Abs(fpos) > obj.InFocusRange) // Do move rels until we converge
                    {
                        iter++;
                        ATF.ATF_ReadPosition(out fpos);
                        var distToFocus = -fpos * obj.SlopeInMicrometers;
                        _focusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
                        if (iter > 10)
                        {
                            Console.WriteLine("Could not converge, check objective slope using MeasureSlope.");
                        }
                    }
                }
        }
    }

        /// <summary>
        /// Moves to a known minimum position and the searches upwards for the first surface to focus on.
        /// Focus must previously be set using Objective.SetFocus() or manually via the autofocus console
        /// </summary>
        public static bool AFOnce(Objective obj)
        {
            int stageLatency = 9; // Stage latency in ms
            int sensorLatency = 3; // Sensor polling latency, ms

            //Always approach the sample from below
            _focusAxis.MoveAbsolute(MINIMUM_FOCUS, Units.Length_Millimetres);

            ATF.ATF_ReadPosition(out float fpos);
            
            if (Math.Abs(fpos) > obj.SensorRange || fpos==0) // We are outside the measurment range
            {
                // Speed that would exceed the sensor range before we read another point
                double approachSpeed = (0.5*obj.SensorRange * obj.SlopeInMicrometers) / (sensorLatency + stageLatency);
                //Move towards the sample
                _focusAxis.MoveVelocity(approachSpeed, Units.Velocity_MillimetresPerSecond);

                while ((Math.Abs(fpos) > 0.9 * obj.SensorRange) || fpos == 0)
                {
                    ATF.ATF_ReadPosition(out fpos);
                }

                _focusAxis.Stop();
            }
            int iter = 0;
            while  (Math.Abs(fpos) > obj.InFocusRange) // Do move rels until we converge
            {
                iter++;
                ATF.ATF_ReadPosition(out fpos);
                var distToFocus = -fpos * obj.SlopeInMicrometers;
                _focusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
                if (iter > 10) {
                    Console.WriteLine("Could not converge, check objective slope using MeasureSlope.");
                    return false;
                }
            }
            return true;
        }

        private static Axis _focusAxis;
        private ObjectiveChanger _objectiveChanger;
    }
}

