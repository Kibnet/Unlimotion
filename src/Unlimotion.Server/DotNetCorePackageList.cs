using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Version = SemanticVersioning.Version;

namespace Unlimotion.Server
{
    class DotNetCorePackageList : IHaveVersions
    {
        private ReadOnlyCollection<Version> list;

        public IEnumerable<Version> Versions { get
        {
            if (list == null)
            {
                list = new ReadOnlyCollection<Version>(GetNETCorePackageList().ToList());
            }
            return list;
        } }

        private IEnumerable<Version> GetNETCorePackageList()
        {
            var versions = new List<Version>();

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo(@"dotnet", "--list-runtimes")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                },
                EnableRaisingEvents = true
            };

            proc.OutputDataReceived += (sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    var match = Regex.Match(e.Data, "(?<Name>Microsoft\\.NETCore\\.App)\\s+(?<Version>([0-9a-z-]+\\.)+[0-9a-z-]+)");
                    if (match.Success)
                    {
                        versions.Add(new Version(match.Groups["Version"].Value));
                    }
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            //Задержка до получения списка (он получаестся асинхронно, что приводит программу к вылету)
            proc.WaitForExit();
            return versions;
        }
    }
}
