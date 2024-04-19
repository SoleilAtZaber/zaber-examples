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
        public Objective(short initialObjectiveNumber)
        {
            _index = initialObjectiveNumber;
        }

        public int InFocusRange => _inFocusRange;

        public float SlopeInMicrometers { get => _slopeInMicrometers; private set => _slopeInMicrometers = value; }

        public int SensorRange => _sensorRange;

        public void SetFocus(Axis focusAxis)
        {
            ATF.ATF_Make0();
            Console.WriteLine("Current focus position: {0} µm", focusAxis.GetPosition(Units.Length_Micrometres));
            return;
        }

        public bool SetAll(int objectiveNumber)
        {
            _index = (short)objectiveNumber;
            int ecode = 0;
            ecode += ATF.ATF_ReadInfocusRange(objectiveNumber, out _inFocusRange);
            ecode += ATF.ATF_ReadSlopeUmPerOut(objectiveNumber, out _slopeInMicrometers);
            ecode += ATF.ATF_ReadLinearRange(objectiveNumber, out _sensorRange);
            return (ecode == 0); // No errors
        }

        public bool MeasureSlope(Axis focus)
        {
            Console.WriteLine("Move to best focus manually, hit any key to proceed");
            Console.ReadKey();
            ATF.ATF_Make0();
            double pos = focus.GetPosition(Units.Length_Micrometres);
            double delta = InFocusRange * SlopeInMicrometers * 0.2;
            List<float> slopes = new();
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
                    Console.WriteLine("{0}um/DN @ {1} um", newSlope, focus.GetPosition(Units.Length_Micrometres) - pos);
                }
            }

            float avgSlope = slopes.Average();
            Console.WriteLine("New average: {0} Current slope: {1} um/DN", avgSlope, SlopeInMicrometers);
            Console.WriteLine("Enter y to overwrite with new measured slope");
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
        private float _slopeInMicrometers;
        private int _sensorRange;
        private int _inFocusRange;
    }
}
