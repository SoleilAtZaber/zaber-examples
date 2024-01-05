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
using System.Windows.Forms.DataVisualization.Charting;
using System.Windows.Forms;
using System.Drawing;

namespace laserAF
{
    internal class Program
    {
        public Zaber.Motion.Ascii.Axis FocusAxis { get; set; } = null;
        public Zaber.Motion.Ascii.Axis XAxis { get; set; } = null;
        
        
        public float umConversion { get; set; } = 0;

        public ObjectiveChanger MOR = null;


       static void Main()
        //Initialize connections
        {
                Program test = new Program();
                test.TestAutofocus();
        }
        public void TestAutofocus()
        {
            var curObjective = new objective(0);
        using (var connection = Connection.OpenSerialPort("COM4"))
        {
            connection.EnableAlerts();

            var deviceList = connection.DetectDevices();

            this.FocusAxis = deviceList[2].GetAxis(1);
            var stage = deviceList[4];
            this.XAxis = stage.GetAxis(1);
            //MOR = ObjectiveChanger.Find(connection);
            int ecode;
            //Open TCP/IP connection to sensor
            Console.WriteLine("Test");
            ecode = ATF.ATF_OpenConnection("169.254.64.162", 27);
            Console.WriteLine("Connection Ack:" + ATF.ATF_PingAck());
            //Test all the various AF modes


            // Move to default focus
           // FocusAxis.Home();
            FocusAxis.MoveAbsolute(19, Units.Length_Millimetres);

            //Turn on the laser, logging, and auto power levelling
            //ATF.ATF_openLogFile("Log.txt", "w");
            ATF.ATF_DisableAutoOff();
            ATF.ATF_EnableLaser();
            ATF.ATF_LaserTrackOn();
            ChangeObjectives(1, ref curObjective);

            //Set the focus point
            //Console.WriteLine("Move the stage to best focus position and then press any key");
            //Console.ReadKey();

           // ATF.ATF_Make0();
            Console.WriteLine(String.Format("True focus pos: {0}", FocusAxis.GetPosition(Units.Length_Micrometres)));
                //TestRefreshRate(); //See how fast the sensor can be updated

                //curObjective.measureSlope(FocusAxis);

            //Intentionally defocus
            //FocusAxis.MoveRelative(-100, Units.Length_Micrometres);
                //Try a single focus point, change objective and then refocus
            FastAF(curObjective, out _);
                //Console.WriteLine("Testing high speed AF");
                TestAFSpeed(curObjective);

                var cts = new CancellationTokenSource();

                //Task moveRel = XAxis.MoveRelativeAsync(100, Units.Length_Millimetres, velocity: 1, velocityUnit: Units.Velocity_MillimetresPerSecond);
                Console.WriteLine("Autofocusing, press any key to quit");
                ContinuousAf(cts.Token, curObjective);// Start continous AF for a few seconds then cancel it
                
                //Wait for user to quit
                Console.ReadKey();
                cts.Cancel();
                //Cleanup
                ATF.ATF_CloseConnection();
        }
        }

        public bool ChangeObjectives(int obj, ref objective current)
        {
            Console.WriteLine("Manually change to:"+ obj);
            Console.ReadKey();
            //MOR.Change(obj);
            if (ATF.ATF_WriteObjNum(obj) == 0)
            {
                current.setAll(obj);
                return true;
            }
            return false;
        }

        public async Task<int> ContinuousAf(CancellationToken cancellationToken, objective obj, float maxLimit = 21, float minLimit = 15)
        {
            double currentPos = FocusAxis.GetPosition(Units.Length_Micrometres);
            
            float fpos = 0;
            int iter = 0;
            int ecode = 0;
            var watch = new Stopwatch();
            while (true)
            {
                var encoderPos = FocusAxis.Settings.Get(SettingConstants.Encoder1Pos, unit: Units.Length_Micrometres);
                ecode = ATF.ATF_ReadPosition(out fpos);

                if (ecode == 0)
                {
                    if (Math.Abs(fpos) > obj.inFocusRange)
                    { //Start the focus move if greater than infocus range. 
                        FocusAxis.MoveAbsoluteAsync(-fpos *obj.umSlope + encoderPos, Units.Length_Micrometres); //Do absolute moves here so that behaviour is defined, continue polling in motion
                                                                                                                 //A sequence of move rels will change depend on the pos when the command was recieved                                                                                  
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

        public float TestAFSpeed(objective obj)
        {
            float ms = 0;
            float residual = 0;
            Random rnd = new Random();
            double pos = FocusAxis.GetPosition(Units.Length_Micrometres);
            // Create a new chart object
            Chart chart = new Chart();

            // Set the chart area
            ChartArea chartArea = new ChartArea();
            chartArea.AxisX.Minimum = 0;
            chartArea.AxisX.Maximum = 100;
            chartArea.AxisY.Minimum = -15;
            chartArea.AxisY.Maximum = 15;
            

            // Add a series to the chart
            Series series = new Series();
            Series error = new Series();
            series.LegendText = "Measured residual";
            error.LegendText = "Stage Error";
            series.ChartType = SeriesChartType.Point;
            error.ChartType= SeriesChartType.Point;
            chartArea.AxisX.Title = "Trial";
            chartArea.AxisY.Title = "Distance [um]";
            chart.ChartAreas.Add(chartArea);
            chart.Series.Add(series);
            chart.Series.Add(error);


            Form form = new Form();
            form.ClientSize = new Size(1000, 600);
            chart.Dock = DockStyle.Fill;
            form.Controls.Add(chart);

            for (int i = 0; i < 1000; i++)
            {
                FocusAxis.MoveAbsolute(pos,Units.Length_Micrometres);
                FocusAxis.MoveRelative((rnd.NextDouble()-0.5) * 600, Units.Length_Micrometres); //random moves up to +-100um
                ms+=FastAF(obj, out residual);
                Thread.Sleep(500);
                series.Points.AddXY(i, residual);
                error.Points.AddXY(i, FocusAxis.GetPosition(Units.Length_Micrometres)-pos);
            }
            // Refresh the chart
            chart.Invalidate();
            chart.Update();

            // Display the form
            Application.Run(form);

            Console.WriteLine($"Avg time: {ms / 1000} ms");
            return ms / 100;
        }

        public void AFOnce(bool safetyStop, int obj)
        {
            //measured distance 
            float fpos = 0;
            int linRange = 30;
            var watch = new Stopwatch();
            var timer = new Stopwatch();
            int stageLatency = 9; //Stage latency in ms
            int workingDistance = 900; //Objecive working distance minimum, um
            var encoderPos = new GetAxisSetting() { Setting = SettingConstants.Encoder1Pos, Unit = Units.Length_Micrometres };
            var velocityUms = new GetAxisSetting() { Setting = SettingConstants.Vel, Unit = Units.Velocity_MicrometresPerSecond };
            //int obj = MOR.GetCurrentObjective();
            double pos = FocusAxis.GetPosition(Units.Length_Micrometres); // Where we are at right now

            ATF.ATF_ReadPosition(out fpos);
            //May not be necessary if we keep the default linRange of 30
            ATF.ATF_ReadLinearRange(obj, out linRange);
            float distToFocus = -fpos * this.umConversion;

            //Console.WriteLine("Dist to focus:"+distToFocus);
            //Console.WriteLine("Linear range:"+linRange * this.umConversion);
            timer.Start(); 
            if (Math.Abs(fpos) > linRange)
            {
                double avoidCrash = 0.1*(linRange * this.umConversion + workingDistance) / (3 + stageLatency); //Speed that would exceed linear range + working distance before we read another point.
                //Console.WriteLine($"Max speed to avoid crashing {avoidCrash} mm/s]");
                //Console.WriteLine("Outside of linear range, moving at speed towards focus");
                FocusAxis.MoveVelocity(Math.Sign(-fpos) * avoidCrash, Units.Velocity_MillimetresPerSecond);

                //We are outside the linear range of the sensor, proceed with polling the sensor at max rate
                while ((Math.Abs(fpos) > 0.5*linRange))
                {
                    ATF.ATF_ReadPosition(out fpos);
                }
                    FocusAxis.Stop();
                    while (Math.Abs(fpos)>2)
                    {
                        ATF.ATF_ReadPosition(out fpos);
                        distToFocus = -fpos * this.umConversion;
                        //Console.WriteLine("dist to focus" + distToFocus);
                        FocusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
                    }
            }
            else //Simple case when in linear range
            {
                FocusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
            }

            // read status
            timer.Stop();
            if (Math.Abs(fpos) > 3)
            {
                ATF.ATF_ReadPosition(out fpos);
                Console.WriteLine($"Focus failed, distance {fpos * this.umConversion}um");
                Console.WriteLine($"Focus pos: {FocusAxis.GetPosition(Units.Length_Micrometres)}");
            }
            else
            {
                Console.WriteLine($"Delta: {fpos * this.umConversion}um");
                Console.WriteLine($"Focus successful, pos: {FocusAxis.GetPosition(Units.Length_Micrometres)}");
            }
            Console.WriteLine("Time to focus:"+timer.ElapsedMilliseconds);
        }
        public float FastAF(objective obj, out float residual)
        {
            //measured distance 
            float fpos = 0;
            var watch = new Stopwatch();
            var timer = new Stopwatch();
            int stageLatency = 9; //Stage latency in ms
            var encoderPos = new GetAxisSetting() { Setting = SettingConstants.Encoder1Pos, Unit = Units.Length_Micrometres };
            var velocityUms = new GetAxisSetting() { Setting = SettingConstants.Vel, Unit = Units.Velocity_MicrometresPerSecond };
            timer.Start();

            ATF.ATF_ReadPosition(out fpos);
            float distToFocus = -fpos * obj.umSlope;
            if (Math.Abs(fpos) < obj.inFocusRange)
            {
                Console.WriteLine("Already in focus");
                residual = fpos * obj.umSlope;
                return 0;
            }
            else if (Math.Abs(fpos) < obj.linearRange)
            {
                Console.WriteLine("Linear,{0}um",distToFocus);
                FocusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
            }

            else if (Math.Abs(fpos) > obj.linearRange) // Outside linear
            {
                Console.WriteLine("Outside linear range");
                double avoidOvershoot = (1.9*obj.linearRange * obj.umSlope) / (3 + stageLatency); //Speed that would exceed 2* linear range before we read another point.
                //Console.WriteLine($"Max speed to avoid overshooting {avoidOvershoot} mm/s]");
                 
                int moveDir = Math.Sign(-fpos);
                FocusAxis.MoveVelocity(moveDir * avoidOvershoot, Units.Velocity_MillimetresPerSecond);

                while ((Math.Abs(fpos) > obj.linearRange))
                {
                   ATF.ATF_ReadPosition(out fpos);
                   if( Math.Sign(-fpos)!= moveDir) {
                        Console.WriteLine("Overshot focus");
                        FocusAxis.Stop();
                        residual = -fpos * obj.umSlope;
                        return -1;
                    }
                }
                FocusAxis.Stop();
                ATF.ATF_ReadPosition(out fpos);
                distToFocus = -fpos * obj.umSlope;
                FocusAxis.MoveRelative(distToFocus, Units.Length_Micrometres);
                
                /*
                //Try to set a target without slowing down
                watch.Start(); //Measure how long we are moving before we get a response from the stage
                GetAxisSettingResult[] stageData = FocusAxis.Settings.GetSynchronized(encoderPos, velocityUms); //Grab the current encoder position and velocity
                watch.Stop();
                double posAtReading = stageData[0].Value - (stageData[1].Value * watch.Elapsed.TotalSeconds); //Estimate of position at time of sensor read
                FocusAxis.MoveAbsolute(-fpos * obj.umSlope + posAtReading, Units.Length_Micrometres);
                //Console.WriteLine($"delta: {-fpos * obj.umSlope}");
                Console.WriteLine($"Delta between ATF read and stage response {watch.Elapsed.TotalMilliseconds}ms , {stageData[1].Value * watch.Elapsed.TotalSeconds}um");
                //We might have to do something here to make the reading converge
                */
            }

            timer.Stop();
            ATF.ATF_ReadPosition(out fpos);
            Console.WriteLine($"Stage pos: {FocusAxis.GetPosition(Units.Length_Micrometres)}");
            Console.WriteLine("Time to focus:" + timer.ElapsedMilliseconds); 
            residual = -fpos * obj.umSlope; 
            Console.WriteLine($"Residual: {residual}um");
            return timer.ElapsedMilliseconds;
        }

       


    }
    class objective
    {
        public short index;
        public short mag;
        public float umSlope;
        public int inFocusRange;
        public int linearRange;

        public objective(short index) {
            this.index = index;
        }
        public bool setAll(int obj) {
            this.index = (short)obj;
            int ecode=0;
            ecode +=ATF.ATF_ReadMagnification(obj, out this.mag);
            ecode += ATF.ATF_ReadLinearRange(obj, out this.linearRange);
            ecode += ATF.ATF_ReadSlopeUmPerOut (obj, out this.umSlope);
            ecode += ATF.ATF_ReadInfocusRange (obj, out this.inFocusRange);
                if (ecode==0)return true; //No errors
                else return false;
        }

        public bool measureSlope(Zaber.Motion.Ascii.Axis focus)
        {
            float slope = 0;
            Console.WriteLine("Move to best focus manually, hit any key to proceed");
            Console.ReadKey();
            double pos=focus.GetPosition(Units.Length_Micrometres);
            double delta = this.linearRange*this.umSlope * 0.2;
            float measured;
            List<float> slopes= new List<float>();
            for (int dir = -1; dir <= 1; dir += 2)
            {
                focus.MoveAbsolute(pos, Units.Length_Micrometres);
                for (int i = 1; i < 5; i++)
                {
                    focus.MoveRelative(dir*delta,Units.Length_Micrometres);
                    ATF.ATF_ReadPosition(out measured);
                    float newSlope = (float)(focus.GetPosition(Units.Length_Micrometres)-pos) / measured;
                    slopes.Add(newSlope);
                    Console.WriteLine("{0}um/px @ {1} um", newSlope, focus.GetPosition(Units.Length_Micrometres)-pos);
                }
            }
            float avgSlope=slopes.Average();
            Console.WriteLine("New average: {0} Current slope: {1} um/px", avgSlope, this.umSlope);
            Console.WriteLine("Want to write new measured slope Y/N");
            string res = Console.ReadLine();

            if (res == "Y")
            {
                ATF.ATF_WriteSlopeUmPerOut(this.index, slope);
                this.umSlope = avgSlope;
                return true;
            }
                return false;
        }

        //Make 0 function?

    }
}

