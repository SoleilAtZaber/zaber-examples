using System;
using Zaber.Motion;
using Zaber.Motion.Ascii;
using Zaber.Motion.Microscopy;
using WDI;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Linq;


namespace laserAF
{
    internal class Program
    {
        public Zaber.Motion.Ascii.Axis FocusAxis { get; set; } = null;
        public Zaber.Motion.Ascii.Axis XAxis { get; set; } = null;


        public float umConversion { get; set; } = 0;

        public ObjectiveChanger MOR = null;


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
                this.FocusAxis = LDA.GetAxis(1);
                //var stage = deviceList[4];
                //this.XAxis = stage.GetAxis(1);
                //MOR = ObjectiveChanger.Find(connection);
                int ecode;

                //Open TCP/IP connection to sensor
                ecode = ATF.ATF_OpenConnection("169.254.64.162", 27);
                Console.WriteLine("Connection Ack:" + ATF.ATF_PingAck());
                //Test all the various AF modes


                // Move to default focus
                if (!FocusAxis.IsHomed()) FocusAxis.Home();

                //Setup Laser AF
                ATF.ATF_DisableAutoOff();
                ATF.ATF_EnableLaser();
                ATF.ATF_LaserTrackOn();
                LDA.GenericCommand("trigger 1 action a user.vdata.0 = setting encoder.pos");
                LDA.GenericCommand("trigger 1 action b stop");
                LDA.GenericCommand("trigger 1 when io di 1 == 1");
                //TestRefreshRate(); //See how fast the sensor can be updated
                ChangeObjectives(1, ref curObjective);

                //Set the focus point for the currentObjective
                curObjective.MeasureFocus(FocusAxis);

                Console.WriteLine(String.Format("True focus pos: {0}", FocusAxis.GetPosition(Units.Length_Micrometres)));


                //curObjective.objectivePairing(FocusAxis);

                Console.WriteLine("Testing high speed AF");
                TestAFSpeed(curObjective);

                var cts = new CancellationTokenSource();

                Console.WriteLine("Autofocusing, press any key to quit");
                ContinuousAf(cts.Token, curObjective);// Start continous AF for a few seconds then cancel it

                //Wait for user to quit
                Console.ReadKey();
                cts.Cancel();
                //Cleanup
                ATF.ATF_CloseConnection();
            }
        }

        public bool ChangeObjectives(int obj, ref Objective current)
        {
            Console.WriteLine("Manually change to:" + obj);
            Console.ReadKey();
            //MOR.Change(obj);
            if (ATF.ATF_WriteObjNum(obj) == 0)
            {
                current.SetAll(obj);
                return true;
            }
            return false;
        }

        public async Task<int> ContinuousAf(CancellationToken cancellationToken, Objective obj, float maxLimit = 21, float minLimit = 15)
        {
            double currentPos = FocusAxis.GetPosition(Units.Length_Micrometres);
            ATF.ATF_Make0();
            ATF.ATF_LaserTrackOn();

            float fpos = 0;
            int iter = 0;
            int ecode = 0;
            var watch = new Stopwatch();
            while (true)
            {
                // var encoderPos = FocusAxis.Settings.Get(SettingConstants.EncoderPos, unit: Units.Length_Micrometres);
                ecode = ATF.ATF_ReadPosition(out fpos);


                if (ecode == 0)
                {
                    if (Math.Abs(fpos) > obj.inFocusRange)
                    {//Start the focus move if greater than infocus range.
                        Console.WriteLine(-fpos * obj.umSlope);
                        FocusAxis.MoveRelative(-fpos * obj.umSlope, Units.Length_Micrometres); //Do absolute moves here so that behaviour is defined, continue polling in motion                                                                                      //A sequence of move rels will change depend on the pos when the command was recieved                                                                                  
                        Thread.Sleep(10);
                    }
                }

                iter += 1;
                if (iter % 1000 == 0)
                {
                    watch.Stop();
                    //Check for cancellation token every few iterations
                    Console.WriteLine($"Focus update rate: {1000 / watch.Elapsed.TotalSeconds} Hz");
                    cancellationToken.ThrowIfCancellationRequested();
                    watch.Reset();
                    watch.Start();
                }
            }
        }

        public long TestRefreshRate()
        {
            float fpos = 0;
            var watch = new Stopwatch();
            watch.Start();
            for (int i = 0; i < 1000; i++)
            {

                ATF.ATF_ReadPosition(out fpos);
            }
            watch.Stop();
            Console.WriteLine($"Refresh Period: {watch.ElapsedMilliseconds / 1000} ms");
            return watch.ElapsedMilliseconds / 1000;
        }

        public float TestAFSpeed(Objective obj)
        {
            float ms = 0;
            Random rand = new Random();

            double pos = FocusAxis.GetPosition(Units.Length_Micrometres);
            FocusAxis.Device.GenericCommand("trigger 1 disable");
            for (int i = -100; i < 100; i++)
            {
                double u1 = 1.0 - rand.NextDouble();
                double u2 = 1.0 - rand.NextDouble();
                double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
                double randNormal = 100 + 50 * randStdNormal;
                FocusAxis.MoveAbsolute(pos, Units.Length_Micrometres);
                FocusAxis.MoveRelative(Math.Sign(i) * randNormal, Units.Length_Micrometres); //+-100um focus moves
                ms += FastAF(obj);
                var error = FocusAxis.GetPosition(Units.Length_Micrometres) - pos;
            }
            Console.WriteLine($"Avg time: {ms / 200} ms");
            return ms / 100;
        }

        public float SW_AF(Objective obj)
        {
            float fpos = 0;
            var timer = new Stopwatch();
            timer.Start();
            int stageLatency = 9; //Stage latency in ms
            int sensorLatency = 3; //Sensor polling latency, ms
            double pos = FocusAxis.GetPosition(Units.Length_Micrometres); // Where we are at right now

            ATF.ATF_ReadPosition(out fpos);
            float distToFocus = -fpos * obj.umSlope;

            if (Math.Abs(fpos) > obj.sensorRange) //We are outside the distance measurment range
            {
                double approachSpeed = (obj.sensorRange * obj.umSlope) / (sensorLatency + stageLatency); //Speed that would exceed linear range before we read another point.

                FocusAxis.MoveVelocity(Math.Sign(-fpos) * approachSpeed, Units.Velocity_MillimetresPerSecond);

                //We are outside the linear range of the sensor, proceed while polling the sensor
                while ((Math.Abs(fpos) > 0.9 * obj.sensorRange))
                {
                    ATF.ATF_ReadPosition(out fpos);
                }
                FocusAxis.Stop();
            }
            while (Math.Abs(fpos) > obj.inFocusRange) //Do move rels until we converge
            {
                ATF.ATF_ReadPosition(out fpos);
                distToFocus = -fpos * obj.umSlope;
                FocusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
            }
            timer.Stop();

            return timer.ElapsedMilliseconds;
        }
        public float FastAF(Objective obj)
        {
            float fpos = 0;
            var timer = new Stopwatch();
            double IOPeriod = 0.2; //Minimum detectable pulse duration
            timer.Start();

            ATF.ATF_ReadPosition(out fpos);

            if (Math.Abs(fpos) < obj.linearFocusRange)
            {
                float distToFocus = -fpos * obj.umSlope;

                FocusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
            }
            else if (Math.Abs(fpos) < obj.inFocusRange)
            {
                Console.WriteLine("Already in focus");
            }

            else if (Math.Abs(fpos) > obj.linearFocusRange)
            {
                FocusAxis.Device.GenericCommand("trigger 1 enable 1"); // Enable for only 1 trigger which saves having to disable later
                double avoidOvershoot = (1.9 * obj.linearFocusRange * obj.umSlope) / IOPeriod; //Speed that would exceed 2* inFocus range before the IO catches it.
                FocusAxis.MoveVelocity(Math.Sign(-fpos) * avoidOvershoot, Units.Velocity_MillimetresPerSecond);
                FocusAxis.WaitUntilIdle();
                double encoderPos = FocusAxis.Device.Settings.Get("user.vdata.0");

                //Alternatively we can poll user.vdata to avoid waiting for settle, is slower EXECPT on USB MCC
                /*double encoderPos = 0;
                while (encoderPos == 0){
                    encoderPos = FocusAxis.Device.Settings.Get("user.vdata.0");
                }
                */

                FocusAxis.MoveAbsolute(encoderPos);
                //FocusAxis.Device.Settings.Set("user.vdata.0",0); Need to reset if polling user.vdata.
                for (int i = 0; i < 3; i++)
                {
                    ATF.ATF_ReadPosition(out fpos);
                    if (Math.Abs(fpos) < obj.inFocusRange) break;
                    Console.WriteLine("Correction step {0}, distance {1}um", i, -fpos * obj.umSlope);
                    FocusAxis.MoveRelative(-fpos * obj.umSlope, Units.Length_Micrometres);
                }
            }
            
            timer.Stop();
            return timer.ElapsedMilliseconds;
        }





    }
    class Objective
    {
        public short index;
        public short mag;
        public float umSlope;
        public int sensorRange;
        public int inFocusRange;
        public int linearFocusRange;
        public double FocusOffset = 0;


        public Objective(short index)
        {
            this.index = index;
        }

        public void MeasureFocus(Axis focus) {
            double pos = focus.GetPosition(Units.Length_Millimetres);
            if ( pos < 18 || pos > 21 ) focus.MoveAbsolute(13, Units.Length_Millimetres); //Move to starting position
            focus.Device.GenericCommand("trigger 1 enable 1"); //Enable the trigger
            focus.MoveVelocity(1, Units.Velocity_MillimetresPerSecond); //Move until we hit approx focus

            //We could implement image-vbased AF here if we wanted to
            Console.WriteLine("Move the stage to best focus position and then press any key");
            Console.ReadKey();

            ATF.ATF_Make0();
            this.FocusOffset = focus.GetPosition(Units.Length_Micrometres);
            return;
        }
        public bool SetAll(int obj)
        {
            this.index = (short)obj;
            int ecode = 0;
            ecode += ATF.ATF_ReadMagnification(obj, out this.mag);
            ecode += ATF.ATF_ReadInfocusRange(obj, out this.inFocusRange);
            ecode += ATF.ATF_ReadSlopeUmPerOut(obj, out this.umSlope);
            ecode += ATF.ATF_ReadInfocusRange(obj, out this.linearFocusRange);
            ecode += ATF.ATF_ReadLinearRange(obj, out this.sensorRange);
            if (ecode == 0) return true; //No errors
            else return false;
        }

        public bool ObjectivePairing(Axis focus)
        {
            Console.WriteLine("Move to best focus manually, hit any key to proceed");
            Console.ReadKey();
            ATF.ATF_Make0();
            double pos = focus.GetPosition(Units.Length_Micrometres);
            double delta = this.inFocusRange * this.umSlope * 0.2;
            float measured;
            List<float> slopes = new List<float>();
            List<double> positions = new List<double>();
            List<double> measurements = new List<double>();
            focus.MoveAbsolute(pos - 2 * this.inFocusRange * this.umSlope, Units.Length_Micrometres);
            for (int i = 1; i < 20; i++)
            {
                focus.MoveRelative(delta, Units.Length_Micrometres);
                ATF.ATF_ReadPosition(out measured);
                var deltaPos = focus.GetPosition(Units.Length_Micrometres) - pos;
                float newSlope = (float)(deltaPos) / measured;
                if (Math.Abs(newSlope) > 0.09)
                {
                    slopes.Add(newSlope);
                    measurements.Add(measured);
                    positions.Add(deltaPos);
                    Console.WriteLine("{0}um/px @ {1} um", newSlope, focus.GetPosition(Units.Length_Micrometres) - pos);
                }
            }

            float avgSlope = slopes.Average();
            Console.WriteLine("New average: {0} Current slope: {1} um/px", avgSlope, this.umSlope);
            Console.WriteLine("Press y to write new measured slope");
            string res = Console.ReadLine();

            if (res == "y")
            {
                ATF.ATF_WriteSlopeUmPerOut(this.index, avgSlope);
                this.umSlope = avgSlope;
                return true;
            }
            return false;
        }


    }
}

