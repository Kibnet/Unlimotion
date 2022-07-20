using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using ServiceStack.Configuration;

namespace Unlimotion.Server
{
    public class AppSettings : AppSettingsBase
    {
        public AppSettings(IConfiguration root, string tier = null) : base(new ConfigurationManagerWrapper(root)) => Tier = tier;

        public override string GetString(string name) => GetNullableString(name);

        private class ConfigurationManagerWrapper : ISettings
        {
            private readonly IConfiguration _root;
            private Dictionary<string, string> _appSettings;

            public ConfigurationManagerWrapper(IConfiguration root) => _root = root;

            private Dictionary<string, string> GetAppSettingsMap()
            {
                if (_appSettings == null)
                {
                    var dictionary = new Dictionary<string, string>();
                    var appSettingsSection = _root.GetSection("appSettings");
                    if (appSettingsSection != null)
                    {
                        foreach (var child in appSettingsSection.GetChildren())
                        {
                            dictionary.Add(child.Key, child.Value);
                        }
                    }
                    _appSettings = dictionary;
                }
                return _appSettings;
            }

            #region Implementation of ISettings

            public string Get(string key) => _root[key];

            public List<string> GetAllKeys() => GetAppSettingsMap().Keys.ToList();

            #endregion
        }
    }
}
