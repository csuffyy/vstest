// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Helpers
{
    using System;
    using System.Diagnostics;

    using Microsoft.VisualStudio.TestPlatform.CrossPlatEngine.Helpers.Interfaces;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel;

    /// <summary>
    /// Helper class to deal with process related functionality.
    /// </summary>
    internal class ProcessHelper : IProcessHelper
    {
        /// <summary>
        /// Launches the process with the arguments provided.
        /// </summary>
        /// <param name="processPath">The path to the process.</param>
        /// <param name="arguments">Process arguments.</param>
        /// <param name="workingDirectory">Working directory of the process.</param>
        /// <returns>The process spawned.</returns>
        /// <exception cref="Exception">Throws any exception that could result as part of the launch.</exception>
        public Process LaunchProcess(string processPath, string arguments, string workingDirectory)
        {
            var process = new Process();
            try
            {
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.WorkingDirectory = workingDirectory;

                process.StartInfo.FileName = processPath;
                process.StartInfo.Arguments = arguments;
                process.EnableRaisingEvents = true;

                process.Start();
            }
            catch (Exception exception)
            {
                process.Dispose();
                process = null;

                EqtTrace.Error("TestHost Process {0} failed to launch with the following exception: {1}", processPath, exception.Message);

                throw;
            }

            return process;
        }

        /// <summary>
        /// Gets the current process file path.
        /// </summary>
        /// <returns> The current process file path. </returns>
        public string GetCurrentProcessFileName()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }
    }
}