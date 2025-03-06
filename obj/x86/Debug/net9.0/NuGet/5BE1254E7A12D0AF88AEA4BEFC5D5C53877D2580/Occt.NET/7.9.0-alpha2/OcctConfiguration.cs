using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VCS_DOCs
{
    public static class OcctConfiguration
    {
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetDefaultDllDirectories(uint directoryFlags);
        //               LOAD_LIBRARY_SEARCH_DEFAULT_DIRS
        private const uint DllSearchFlags = 0x00001000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AddDllDirectory(string lpPathName);

        /// <summary>
        /// Construction of Gdal/Ogr
        /// </summary>
        static OcctConfiguration()
        {
            string executingDirectory = string.Empty, occtPath = string.Empty, nativePath = string.Empty;
            try
            {
                string executingAssemblyFile = new Uri(Assembly.GetExecutingAssembly().GetName().CodeBase).LocalPath;
                executingDirectory = Path.GetDirectoryName(executingAssemblyFile);

                if (string.IsNullOrEmpty(executingDirectory))
                    throw new InvalidOperationException("cannot get executing directory");


                // modify search place and order
                SetDefaultDllDirectories(DllSearchFlags);

                occtPath = Path.Combine(executingDirectory, "occt");
                nativePath = Path.Combine(occtPath, GetPlatform());
                if (!Directory.Exists(nativePath))
                    throw new DirectoryNotFoundException($"Occt native directory not found at '{nativePath}'");
                if (!File.Exists(Path.Combine(nativePath, "TKernel.dll")))
                    throw new FileNotFoundException(
                        $"Occt native wrapper file not found at '{Path.Combine(nativePath, "TKernel.dll")}'");

                // Add directories
                AddDllDirectory(nativePath);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e, "error");
                Trace.WriteLine($"Executing directory: {executingDirectory}", "error");
                Trace.WriteLine($"occt directory: {occtPath}", "error");
                Trace.WriteLine($"native directory: {nativePath}", "error");

                //throw;
            }
        }
        

        /// <summary>
        /// 
        /// </summary>
        public static void Configure()
        {
        }
        
        /// <summary>
        /// Function to determine which platform we're on
        /// </summary>
        private static string GetPlatform()
        {
            return Environment.Is64BitProcess ? "x64" : "x86";
        }
    }
}