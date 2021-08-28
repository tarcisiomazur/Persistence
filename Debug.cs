namespace Persistence
{
    public class Debug
    {
        public static void WriteLine(string msg)
        {
            System.Diagnostics.Trace.WriteLine(msg);
        }
    }
}