using System;
using Zaber.Motion;
using Zaber.Motion.Ascii;
using Zaber.Motion.Microscopy;
using WDI;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace laserAF
{
    internal class Program
    {
        public Axis FocusAxis { get; set; } = null;
        public Axis XAxis { get; set; } = null;
        public ATF AF;
        public float umConversion { get; set; } = 0;

        public ObjectiveChanger MOR = null;

        private void Main()
        //Initialize connections
        {
            using (var connection = Connection.OpenSerialPort("COM3"))
            {
                connection.EnableAlerts();

                var deviceList = connection.DetectDevices();

                FocusAxis = deviceList.FirstOrDefault(x => Regex.IsMatch(x.Name, "LDA")).GetAxis(1);
                var stage = deviceList.FirstOrDefault(x => Regex.IsMatch(x.Name, "A[DS]R"));
                XAxis = stage.GetAxis(1);
                MOR = ObjectiveChanger.Find(connection);
                int ecode;
                //Open TCP/IP connection to sensor
                ecode = ATF.ATF_OpenConnection("192.168.1.10", 27);
                Console.WriteLine("Connection Ack:", ATF.ATF_PingAck() == (int)WDI.AtfCodesEnm.AfStatusOK);
                TestAutofocus();
            }
        }
        public async void TestAutofocus()
        {
            //Test all the various AF modes


            // Move to default focus
            FocusAxis.Home();
            FocusAxis.MoveAbsolute(20, Units.Length_Millimetres);

            //Turn on the laser, logging, and auto power levelling
            ATF.ATF_openLogFile("Log.txt","w");
            ATF.ATF_setLogLevel(3);
            ATF.ATF_DisableAutoOff();
            ATF.ATF_EnableLaser();
            ATF.ATF_LaserTrackOn();

            //Set the focus point
            Console.WriteLine("Move the stage to best focus position and then press any key");
            Console.ReadKey();

            ATF.ATF_Make0();
            Console.WriteLine(String.Format("True focus pos: {0}", FocusAxis.GetPosition(Units.Length_Micrometres)));
            TestRefreshRate(); //See how fast the sensor can be updated

            //Intentionally defocus
            FocusAxis.MoveRelative(-100, Units.Length_Micrometres);
            //Try a single focus point, change objective and then refocus
            await AFOnce(true);
            Thread.Sleep(1000);
            ChangeObjectives(2);

            await AFOnce(false);
            Thread.Sleep(1000);
            Console.WriteLine("Intentionally big defocus");
            FocusAxis.MoveRelative(-1500, Units.Length_Micrometres);
            await AFOnce(false);

            // Start continous AF for a few seconds then cancel it
            var cts = new CancellationTokenSource();
           
            Task moveRel=XAxis.MoveRelativeAsync(100, Units.Length_Millimetres, velocity: 1, velocityUnit: Units.Velocity_MillimetresPerSecond);
            Task.WaitAny(moveRel, ContinuousAf(cts.Token));
            cts.Cancel();
            //Cleanup
            ATF.ATF_closeLogFile();
            ATF.ATF_CloseConnection();
        }

        public bool ChangeObjectives(int obj)
        {
            float fumout = 0;
            MOR.Change(obj);
            if (ATF.ATF_WriteObjNum(obj) == 0)
            {
                ATF.ATF_ReadSlopeUmPerOut(obj, out fumout);
                umConversion = fumout;
                return true;
            }
            return false;
        }

        public async Task<int> ContinuousAf(CancellationToken cancellationToken, int focusTolerance = 5, float maxLimit = 21, float minLimit = 15)
        {
            double currentPos = FocusAxis.GetPosition(Units.Length_Micrometres);
            float fpos = 0;
            int iter = 0;
            int ecode = 0;
            int objective=MOR.GetCurrentObjective();
            ATF.ATF_ReadInfocusRange(objective, out focusTolerance);
            var watch = new Stopwatch();
            while (true)
            {
                var encoderPos = FocusAxis.Settings.Get(SettingConstants.Encoder1Pos, unit: Units.Length_Micrometres);
                ecode = ATF.ATF_ReadPosition(out fpos);

                if (ecode == 0)
                {
                    if (Math.Abs(fpos) > focusTolerance)
                    { //Start the focus move if greater than infocus range. 
                        FocusAxis.MoveAbsoluteAsync(fpos * umConversion+encoderPos, Units.Length_Micrometres); //Do absolute moves here so that behaviour is defined.
                                                                                                               //A sequence of move rels will change depend on the pos when the command was recieved                                                                                  
                    }
                }

                iter += 1;
                if (iter % 1000 == 0)
                {
                    watch.Stop();
                    //Check for cancellation token every few iterations
                    Console.WriteLine($"Focus update rate: {1000/watch.Elapsed.TotalSeconds} Hz");
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
            for (int i = 0; i < 1000; i++) {

                ATF.ATF_ReadPosition(out fpos);
            }
            watch.Stop();
            Console.WriteLine($"Refresh Period: {watch.ElapsedMilliseconds/1000} ms");
            return watch.ElapsedMilliseconds/1000;
        }

        public async Task AFOnce(bool safetyStop)
        {
            //measured distance 
            float fpos = 0;
            int linRange = 30;
            var watch = new Stopwatch();
            int stageLatency = 9; //Stage latency in ms
            int workingDistance = 900; //Objecive working distance minimum, um
            var encoderPos = new GetAxisSetting() { Setting = SettingConstants.Encoder1Pos, Unit = Units.Length_Micrometres };
            var velocityUms = new GetAxisSetting() { Setting = SettingConstants.Vel, Unit = Units.Velocity_MicrometresPerSecond };
            int obj = MOR.GetCurrentObjective();
            double pos = FocusAxis.GetPosition(Units.Length_Micrometres); // Where we are at right now

            ATF.ATF_ReadPosition(out fpos);
            //May not be necessary if we keep the default linRange of 30
            ATF.ATF_ReadLinearRange(obj, out linRange);
            float distToFocus = fpos * umConversion;

            Console.WriteLine(distToFocus);
            Console.WriteLine(linRange);

            if (Math.Abs(fpos) > linRange)
            {
                float avoidCrash = (linRange * umConversion + workingDistance) / (TestRefreshRate() + stageLatency); //Speed that would exceed linear range + working distance before we read another point.
                Console.WriteLine($"Max speed to avoid crashing {avoidCrash} mm/s]");
                Console.WriteLine("Outside of linear range, moving at speed towards focus");
                await FocusAxis.MoveVelocityAsync(Math.Sign(fpos) * avoidCrash, Units.Velocity_MillimetresPerSecond);

                //We are outside the linear range of the sensor, proceed with polling the sensor at max rate
                while ((Math.Abs(fpos) > linRange))
                {
                    ATF.ATF_ReadPosition(out fpos);
                }
                if (safetyStop)
                {
                    FocusAxis.Stop();
                    Thread.Sleep(1000);
                    ATF.ATF_ReadPosition(out fpos);
                    FocusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
                }
                else
                {
                    watch.Start(); //Measure how long we are moving before we get a response from the stage
                    GetAxisSettingResult[] stageData = await FocusAxis.Settings.GetSynchronizedAsync(encoderPos, velocityUms); //Grab the current encoder position and velocity
                    watch.Stop();
                    double estimatedPos = stageData[0].Value - (stageData[1].Value * watch.Elapsed.TotalSeconds); //Estimate of position at time of sensor read
                    await FocusAxis.MoveAbsoluteAsync(fpos * umConversion + estimatedPos, Units.Length_Micrometres);
                    Console.WriteLine($"Measured distance = {fpos * umConversion}");
                    Console.WriteLine($"Delta between ATF read and stage response {watch.Elapsed.TotalMilliseconds}s , {stageData[1].Value}um/s, {stageData[1].Value * watch.Elapsed.TotalSeconds}um");
                    //We might have to do something here to make the reading converge
                }
            }
            else //Simple case when in linear range
            {
                await FocusAxis.MoveRelativeAsync(distToFocus, Units.Length_Micrometres);
            }
           
            short atf_status;
            // read status
            if (ATF.ATF_ReadStatus(out atf_status) != (int)WDI.StatusFlagsEnm.MsInFocus)
            {
                ATF.ATF_ReadPosition(out fpos);
                Console.WriteLine($"Focus failed, distance {fpos * umConversion}um");
            }
            else Console.WriteLine($"Focus successful, pos: {FocusAxis.GetPosition(Units.Length_Micrometres)}");
        }


    }
}

