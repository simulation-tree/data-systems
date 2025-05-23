using System;

namespace Data.Systems
{
    internal struct LoadingTask
    {
        public readonly DateTime startTime;
        public double duration;

        [Obsolete("Default constructor not supported", true)]
        public LoadingTask()
        {
            throw new NotSupportedException();
        }

        public LoadingTask(DateTime startTime)
        {
            this.startTime = startTime;
        }
    }
}