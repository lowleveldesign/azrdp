using System.Diagnostics;

namespace LowLevelDesign.AzureRemoteDesktop
{
    static class Logger
    {
        public static readonly TraceSource Log;

        static Logger()
        {
            Log = new TraceSource("azrdp", SourceLevels.Information);
            Log.Listeners.Add(new ConsoleTraceListener());
        }

        public static SourceLevels Level
        {
            get { return Log.Switch.Level; }
            set { Log.Switch.Level = value; }
        }
    }
}
