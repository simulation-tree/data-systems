using System;

namespace Data.Systems
{
    internal struct LoadingTask
    {
        public readonly double startTime;
        public double duration;

        [Obsolete("Default constructor not supported", true)]
        public LoadingTask()
        {
            throw new NotSupportedException();
        }

        public LoadingTask(double startTime)
        {
            this.startTime = startTime;
            duration = 0;
        }
    }
}