using System;
using System.Linq;

namespace Unlimotion.Server
{
    public class DotNetVersionHelper
    {
        private readonly IHaveVersions iHaveVersions;

        public DotNetVersionHelper(IHaveVersions iHaveVersions) => this.iHaveVersions = iHaveVersions;

        /// <summary>
        /// Получение наиболее подходящего Runtime .NET
        /// </summary>
        public string GetNearestDotNetVersion(string targetVersion)
        {
            var target = new SemanticVersioning.Version(targetVersion);
            var range = new SemanticVersioning.Range($"^{target.Major}.{target.Minor}.x");
            var versions = range.Satisfying(iHaveVersions.Versions, true).ToList();
            if (!versions.Any()) return "";
            try
            {
                return range.MaxSatisfying(versions, true).ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return "";
            }
        }
    }
}
