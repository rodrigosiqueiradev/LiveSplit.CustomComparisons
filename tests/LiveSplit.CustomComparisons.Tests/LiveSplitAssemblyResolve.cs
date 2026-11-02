using System;
using System.IO;
using System.Reflection;

namespace LiveSplit.CustomComparisons.Tests
{
    /// <summary>
    /// LiveSplit.Core references DLLs that ship next to LiveSplit.exe. The test build copies
    /// them beside the test assembly; this picks them up if the host looks elsewhere first.
    /// </summary>
    internal static class LiveSplitAssemblyResolve
    {
        internal static void Register()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                string file = new AssemblyName(args.Name).Name + ".dll";
                string beside = Path.Combine(AppContext.BaseDirectory, file);
                if (File.Exists(beside))
                {
                    return Assembly.LoadFrom(beside);
                }

                string dir = Environment.GetEnvironmentVariable("LIVESPLIT_DIR");
                if (string.IsNullOrEmpty(dir))
                {
                    return null;
                }

                string installed = Path.Combine(dir, file);
                return File.Exists(installed) ? Assembly.LoadFrom(installed) : null;
            };
        }
    }
}
