using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;
using Process = System.Diagnostics.Process;

namespace Protobuild.IDE.VisualStudio
{
    public class CrossPlatformProjectWizard : IWizard
    {
        public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary, WizardRunKind runKind, object[] customParams)
        {
            var protobuildManager = replacementsDictionary["ProtobuildManagerExecutablePath"];
            var workingDirectory = replacementsDictionary["ProtobuildManagerWorkingDirectory"];
            var templateUrl = replacementsDictionary["ProtobuildManagerTemplateURL"];

            var projectName = replacementsDictionary["$projectname$"];
            var destinationDirectory = replacementsDictionary["$destinationdirectory$"];
            var solutionDirectory = replacementsDictionary["$solutiondirectory$"];

            if (solutionDirectory != destinationDirectory)
            {
                // We don't want this directory.
                Directory.Delete(destinationDirectory);
            }

            var serializer = new JavaScriptSerializer();
            var data = serializer.Serialize(new
            {
                templateurl = templateUrl,
                prefilledprojectname = projectName,
                prefilleddestinationdirectory = solutionDirectory,
                visualstudiopid = Process.GetCurrentProcess().Id,
            });

            var encodedState = Convert.ToBase64String(Encoding.ASCII.GetBytes(data));

            var processStartInfo = new ProcessStartInfo(protobuildManager,
                "ProjectNamingWorkflow " + encodedState)
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            };
            var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Can't start the Protobuild manager!");
            }

            while (!process.HasExited)
            {
                if (!File.Exists(Path.Combine(solutionDirectory, "Protobuild.exe")) &&
                    new DirectoryInfo(solutionDirectory).GetFiles("*.sln").Length == 0)
                {
                    System.Threading.Thread.Sleep(100);
                }
                else
                {
                    break;
                }
            }
        }

        public void ProjectFinishedGenerating(Project project)
        {
        }

        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
        }

        public bool ShouldAddProjectItem(string filePath)
        {
            return false;
        }

        public void BeforeOpeningFile(ProjectItem projectItem)
        {
        }

        public void RunFinished()
        {
        }
    }
}
