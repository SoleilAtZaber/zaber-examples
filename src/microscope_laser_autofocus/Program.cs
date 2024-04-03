using System;
using System.Diagnostics;
using System.Threading;

using WDI;

using Zaber.Motion;
using Zaber.Motion.Ascii;
using Zaber.Motion.Microscopy;


namespace MicroscopeLaserAF
{
    internal class Program
    {
        static void Main()
        {
            Program test = new Program();
            test.TestAutofocus();
        }

        public void TestAutofocus()
        {
            var curObjective = new Objective(0);
            using (var connection = Connection.OpenSerialPort("COM4"))
            {
                connection.EnableAlerts();

                var deviceList = connection.DetectDevices();
                Device LDA = deviceList[1];
                _focusAxis = LDA.GetAxis(1);
                // FIXME: Lots of commented-out code in this file; delete it or if users are supposed to uncomment it, document when.
                // var stage = deviceList[4];
                // _xAxis = stage.GetAxis(1);
                // _objectiveChanger = ObjectiveChanger.Find(connection);

                int ecode;

                // Open TCP/IP connection to sensor
                ecode = ATF.ATF_OpenConnection("169.254.64.162", 27);
                Console.WriteLine("Connection Ack:" + ATF.ATF_PingAck());
                // Test all the various AF modes


                // Move to default focus
                if (!_focusAxis.IsHomed())
                {
                    _focusAxis.Home();
                }

                // Setup Laser AF
                ATF.ATF_DisableAutoOff();
                ATF.ATF_EnableLaser();
                ATF.ATF_LaserTrackOn();
                var trigger = LDA.Triggers.GetTrigger(1);
                trigger.OnFireSetToSetting(TriggerAction.A, 0, "user.vdata.0", TriggerOperation.SetTo, 1, "encoder.pos");
                trigger.OnFire(TriggerAction.B, 0, "stop");
                trigger.FireWhenIo(IoPortType.DigitalInput, 1, TriggerCondition.EQ, 1);
                // FIXME: Document when users should uncomment this line.
                // TestRefreshRate(); // See how fast the sensor can be updated
                ChangeObjectives(1, ref curObjective);

                // Set the focus point for the currentObjective
                curObjective.MeasureFocus(_focusAxis);

                Console.WriteLine(string.Format("True focus pos: {0}", _focusAxis.GetPosition(Units.Length_Micrometres)));

                // FIXME: Document when users should uncomment this line.
                // curObjective.ObjectivePairing(FocusAxis);

                Console.WriteLine("Testing high speed AF");
                TestAFSpeed(curObjective);

                Console.WriteLine("Autofocusing, press any key to quit");
                ContinuousAf(curObjective);

                // Cleanup
                ATF.ATF_CloseConnection();
            }
        }

        public bool ChangeObjectives(int objectiveNumber, ref Objective current)
        {
            Console.WriteLine("Manually change to:" + objectiveNumber);
            Console.ReadKey();
            // _objectiveChanger.Change(obj);

            if (ATF.ATF_WriteObjNum(objectiveNumber) == 0)
            {
                current.SetAll(objectiveNumber);
                return true;
            }

            return false;
        }

        public void ContinuousAf(Objective obj)
        {
            // double currentPos = _focusAxis.GetPosition(Units.Length_Micrometres); // FIXME: This line had no effect.
            ATF.ATF_Make0();
            ATF.ATF_LaserTrackOn();

            int iter = 0;
            var watch = new Stopwatch();
            while (true)
            {
                // FIXME: This line had no effect.
                // var encoderPos = _focusAxis.Settings.Get(SettingConstants.EncoderPos, unit: Units.Length_Micrometres);
                var ecode = ATF.ATF_ReadPosition(out _);
                if (ecode == 0)
                {
                    if (Math.Abs((float)0) > obj.InFocusRange)
                    {
                        // Start the focus move if greater than infocus range.
                        Console.WriteLine(-(float)0 * obj.SlopeInMicrometers);
                        _focusAxis.MoveRelative(-(float)0 * obj.SlopeInMicrometers, Units.Length_Micrometres); // Do absolute moves here so that behaviour is defined, continue polling in motion                                                                                      //A sequence of move rels will change depend on the pos when the command was recieved                                                                                  
                        Thread.Sleep(10);
                    }
                }

                iter += 1;
                if (iter % 1000 == 0)
                {
                    watch.Stop();
                    // Check for user cancel every few iterations
                    Console.WriteLine($"Focus update rate: {1000 / watch.Elapsed.TotalSeconds} Hz");
                    if (Console.KeyAvailable)
                    {
                        Console.ReadKey();
                        break;
                    }

                    watch.Reset();
                    watch.Start();
                }
            }
        }

        public long TestRefreshRate()
        {
            var watch = new Stopwatch();
            watch.Start();
            for (int i = 0; i < 1000; i++)
            {
                ATF.ATF_ReadPosition(out float fpos);
            }

            watch.Stop();
            Console.WriteLine($"Refresh Period: {watch.ElapsedMilliseconds / 1000} ms");
            return watch.ElapsedMilliseconds / 1000;
        }

        public float TestAFSpeed(Objective obj)
        {
            float ms = 0;
            Random rand = new Random();

            double pos = _focusAxis.GetPosition(Units.Length_Micrometres);
            _focusAxis.Device.GenericCommand("trigger 1 disable");
            for (int i = -100; i < 100; i++)
            {
                double u1 = 1.0 - rand.NextDouble();
                double u2 = 1.0 - rand.NextDouble();
                double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
                double randNormal = 100 + 50 * randStdNormal;
                _focusAxis.MoveAbsolute(pos, Units.Length_Micrometres);
                _focusAxis.MoveRelative(Math.Sign(i) * randNormal, Units.Length_Micrometres); // +-100um focus moves
                ms += FastAF(obj);
                // var error = _focusAxis.GetPosition(Units.Length_Micrometres) - pos; // FIXME: This line had no effect.
            }

            Console.WriteLine($"Avg time: {ms / 200} ms");
            return ms / 100;
        }

        public float SW_AF(Objective obj)
        {
            var timer = new Stopwatch();
            timer.Start();
            int stageLatency = 9; // Stage latency in ms
            int sensorLatency = 3; // Sensor polling latency, ms
            // FIXME: This line had no effect.
            // double pos = _focusAxis.GetPosition(Units.Length_Micrometres); // Where we are at right now

            ATF.ATF_ReadPosition(out float fpos);
            // float distToFocus = -fpos * obj.SlopeInMicrometers;  // FIXME: This line had no effect.

            if (Math.Abs(fpos) > obj.SensorRange) // We are outside the distance measurment range
            {
                double approachSpeed = (obj.SensorRange * obj.SlopeInMicrometers) / (sensorLatency + stageLatency); // Speed that would exceed linear range before we read another point.

                _focusAxis.MoveVelocity(Math.Sign(-fpos) * approachSpeed, Units.Velocity_MillimetresPerSecond);

                // We are outside the linear range of the sensor, proceed while polling the sensor
                while ((Math.Abs(fpos) > 0.9 * obj.SensorRange))
                {
                    ATF.ATF_ReadPosition(out fpos);
                }

                _focusAxis.Stop();
            }

            while (Math.Abs(fpos) > obj.InFocusRange) // Do move rels until we converge
            {
                ATF.ATF_ReadPosition(out fpos);
                var distToFocus = -fpos * obj.SlopeInMicrometers;
                _focusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
            }

            timer.Stop();

            return timer.ElapsedMilliseconds;
        }

        public float FastAF(Objective obj)
        {
            var timer = new Stopwatch();
            double IOPeriod = 0.2; // Minimum detectable pulse duration
            timer.Start();

            ATF.ATF_ReadPosition(out float fpos);

            if (Math.Abs(fpos) < obj.LinearFocusRange)
            {
                float distToFocus = -fpos * obj.SlopeInMicrometers;
                _focusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
            }
            else if (Math.Abs(fpos) < obj.InFocusRange)
            {
                Console.WriteLine("Already in focus");
            }
            else if (Math.Abs(fpos) > obj.LinearFocusRange)
            {
                var trigger = _focusAxis.Device.Triggers.GetTrigger(1);
                trigger.Enable(1); // Enable for only 1 trigger which saves having to disable later
                double avoidOvershoot = (1.9 * obj.LinearFocusRange * obj.SlopeInMicrometers) / IOPeriod; // Speed that would exceed 2* inFocus range before the IO catches it.
                _focusAxis.MoveVelocity(Math.Sign(-fpos) * avoidOvershoot, Units.Velocity_MillimetresPerSecond);
                _focusAxis.WaitUntilIdle();

                double encoderPos = _focusAxis.Device.Settings.Get("user.vdata.0");
                // Alternatively we can poll user.vdata to avoid waiting for settle, is slower EXECPT on USB MCC
                // double encoderPos = 0;
                // while (encoderPos == 0){
                //     encoderPos = _focusAxis.Device.Settings.Get("user.vdata.0");
                // }

                _focusAxis.MoveAbsolute(encoderPos);
                // _focusAxis.Device.Settings.Set("user.vdata.0", 0); Need to reset if polling user.vdata.
                for (int i = 0; i < 3; i++)
                {
                    ATF.ATF_ReadPosition(out fpos);
                    if (Math.Abs(fpos) < obj.InFocusRange)
                    {
                        break;
                    }

                    Console.WriteLine("Correction step {0}, distance {1}um", i, -fpos * obj.SlopeInMicrometers);
                    _focusAxis.MoveRelative(-fpos * obj.SlopeInMicrometers, Units.Length_Micrometres);
                }
            }
            
            timer.Stop();
            return timer.ElapsedMilliseconds;
        }


        private Axis _focusAxis;
        private Axis _xAxis; // FIXME: Unused except by commented-out code; remove if actually not needed.
        private ObjectiveChanger _objectiveChanger; // FIXME: Unused except by commented-out code; remove if actually not needed.
    }
}

