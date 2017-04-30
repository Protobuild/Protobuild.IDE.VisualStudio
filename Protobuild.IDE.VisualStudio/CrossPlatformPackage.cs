using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Management;
using Process = System.Diagnostics.Process;

namespace Protobuild.IDE.VisualStudio
{
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    [Guid("e3556644-2e00-4616-8507-15e3cd889588")]
    [ProvideAutoLoad(UIContextGuids.NoSolution)]
    [ProvideAutoLoad(UIContextGuids.SolutionExists)]
    public sealed class ProtobuildPackage : Package, IVsDebuggerEvents, IVsSolutionEvents
    {
        private IVsSolution2 _solution;
        private uint _solutionEventsCookie;
        private IVsDebugger _debugger;
        private uint _debuggerEventsCookie;
        private DTE _dte;
        private IVsSolutionBuildManager _solutionBuildManager;
        private List<System.Diagnostics.Process> _runningProcesses = new List<System.Diagnostics.Process>();
        private bool _isDebugging;

        protected override void Initialize()
        {
            _dte = GetService(typeof(DTE)) as DTE;

            _solution = ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution)) as IVsSolution2;
            if (_solution != null)
            {
                _solution.AdviseSolutionEvents(this, out _solutionEventsCookie);
            }

            _debugger = ServiceProvider.GlobalProvider.GetService(typeof(SVsShellDebugger)) as IVsDebugger;
            if (_debugger != null)
            {
                _debugger.AdviseDebuggerEvents(this, out _debuggerEventsCookie);
            }

            _solutionBuildManager = ServiceProvider.GlobalProvider.GetService(typeof(IVsSolutionBuildManager)) as IVsSolutionBuildManager;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (_solution != null)
            {
                _solution.UnadviseSolutionEvents(_solutionEventsCookie);
            }
            if (_debugger != null)
            {
                _debugger.UnadviseDebuggerEvents(_debuggerEventsCookie);
            }
        }

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            return VSConstants.S_OK;
        }

        public int OnModeChange(DBGMODE dbgmodeNew)
        {
            if (dbgmodeNew == DBGMODE.DBGMODE_Run)
            {
                if (_isDebugging)
                {
                    return VSConstants.S_OK;
                }

                _isDebugging = true;

                var startupProjects = _dte.Solution.SolutionBuild.StartupProjects as object[];
                if (startupProjects == null)
                {
                    return VSConstants.S_OK;
                }

                var startupProjectPaths = startupProjects.Cast<string>().ToArray();
                foreach (var projectUniqueName in startupProjectPaths)
                {
                    IVsHierarchy hierarchy;
                    _solution.GetProjectOfUniqueName(projectUniqueName, out hierarchy);

                    var activeConfigurations = new IVsProjectCfg[1];
                    if (_solutionBuildManager.FindActiveProjectCfg(IntPtr.Zero, IntPtr.Zero, hierarchy, activeConfigurations) == VSConstants.S_OK)
                    {
                        string configurationName;
                        if (activeConfigurations[0].get_CanonicalName(out configurationName) == VSConstants.S_OK)
                        {
                            var buildPropertyStorage = hierarchy as IVsBuildPropertyStorage;
                            if (buildPropertyStorage != null)
                            {
                                string hostAppExecutable;
                                string hostAppArguments;
                                string hostAppWorkingDirectory;
                                if (buildPropertyStorage.GetPropertyValue("ProtobuildLaunchHostAppExecutable", configurationName, (uint)_PersistStorageType.PST_PROJECT_FILE, out hostAppExecutable) == VSConstants.S_OK &&
                                    buildPropertyStorage.GetPropertyValue("ProtobuildLaunchHostAppArguments", configurationName, (uint)_PersistStorageType.PST_PROJECT_FILE, out hostAppArguments) == VSConstants.S_OK &&
                                    buildPropertyStorage.GetPropertyValue("ProtobuildLaunchHostAppWorkingDirectory", configurationName, (uint)_PersistStorageType.PST_PROJECT_FILE, out hostAppWorkingDirectory) == VSConstants.S_OK)
                                {
                                    string launchPath = null;
                                    if (Path.IsPathRooted(hostAppExecutable))
                                    {
                                        launchPath = hostAppExecutable;
                                    }
                                    else
                                    {
                                        launchPath = Path.Combine(new FileInfo(_dte.Solution.FileName).DirectoryName, hostAppExecutable);
                                    }

                                    try
                                    {
                                        var processStartInfo = new ProcessStartInfo(launchPath, hostAppArguments);
                                        processStartInfo.WorkingDirectory = hostAppWorkingDirectory;
                                        processStartInfo.UseShellExecute = false;
                                        _runningProcesses.Add(Process.Start(processStartInfo));
                                    }
                                    catch
                                    {
                                        // Ignore
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if (dbgmodeNew == DBGMODE.DBGMODE_Design)
            {
                _isDebugging = false;

                // Stop host processes.
                foreach (var process in _runningProcesses)
                {
                    KillProcessAndChildren(process.Id);
                }
                _runningProcesses.Clear();
            }

            return VSConstants.S_OK;
        }

        private static void KillProcessAndChildren(int pid)
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher
              ("Select * From Win32_Process Where ParentProcessID=" + pid);
            ManagementObjectCollection moc = searcher.Get();
            foreach (ManagementObject mo in moc)
            {
                KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
            }
            try
            {
                Process proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (Exception)
            {
                // Process already exited.
            }
        }
    }
}
