using System;
using System.Collections.Generic;
using System.Linq;

using WDI;

using Zaber.Motion;
using Zaber.Motion.Ascii;

namespace MicroscopeLaserAF
{
    public class Objective
    {
        public Objective(short index)
        {
            _index = index;
        }

        public int InFocusRange => _inFocusRange;

        public float SlopeInMicrometers { get => _slopeInMicrometers; private set => _slopeInMicrometers = value; }


        public int SensorRange => _sensorRange;

        public int LinearFocusRange => _linearFocusRange;

        public void MeasureFocus(Axis focus)
        {
            double pos = focus.GetPosition(Units.Length_Millimetres);
            if (pos < 18 || pos > 21)
            {
                focus.MoveAbsolute(13, Units.Length_Millimetres); // Move to starting position
            }

            focus.Device.Triggers.GetTrigger(1).Enable();
            focus.MoveVelocity(1, Units.Velocity_MillimetresPerSecond); // Move until we hit approx focus

            // We could implement image-vbased AF here if we wanted to
            Console.WriteLine("Move the stage to best focus position and then press any key");
            Console.ReadKey();

            ATF.ATF_Make0();
            _focusOffset = focus.GetPosition(Units.Length_Micrometres);
            return;
        }

        public bool SetAll(int obj)
        {
            _index = (short)obj;
            int ecode = 0;
            ecode += ATF.ATF_ReadMagnification(obj, out _mag);
            ecode += ATF.ATF_ReadInfocusRange(obj, out _inFocusRange);
            ecode += ATF.ATF_ReadSlopeUmPerOut(obj, out _slopeInMicrometers);
            ecode += ATF.ATF_ReadInfocusRange(obj, out _linearFocusRange);
            ecode += ATF.ATF_ReadLinearRange(obj, out _sensorRange);
            return (ecode == 0); // No errors
        }

        public bool ObjectivePairing(Axis focus)
        {
            Console.WriteLine("Move to best focus manually, hit any key to proceed");
            Console.ReadKey();
            ATF.ATF_Make0();
            double pos = focus.GetPosition(Units.Length_Micrometres);
            double delta = InFocusRange * SlopeInMicrometers * 0.2;
            List<float> slopes = new List<float>();
            List<double> positions = new List<double>();
            List<double> measurements = new List<double>();
            focus.MoveAbsolute(pos - 2 * InFocusRange * SlopeInMicrometers, Units.Length_Micrometres);
            for (int i = 1; i < 20; i++)
            {
                focus.MoveRelative(delta, Units.Length_Micrometres);
                ATF.ATF_ReadPosition(out var measured);
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
            Console.WriteLine("New average: {0} Current slope: {1} um/px", avgSlope, SlopeInMicrometers);
            Console.WriteLine("Press y to write new measured slope");
            string res = Console.ReadLine();

            if (res == "y")
            {
                ATF.ATF_WriteSlopeUmPerOut(_index, avgSlope);
                SlopeInMicrometers = avgSlope;
                return true;
            }

            return false;
        }


        private short _index;
        private short _mag;
        private float _slopeInMicrometers;
        private int _sensorRange;
        private int _inFocusRange;
        private int _linearFocusRange;
        private double _focusOffset = 0;
    }
}
