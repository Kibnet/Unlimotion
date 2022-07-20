using System.Net.Http;
using Microsoft.Extensions.Configuration;
using ServiceStack;

namespace Unlimotion.Server
{
    public class ServiceStackKey
    {
        IConfiguration configuration;
        ServiceStackSettings settings;

        public void Register(IConfiguration Configuration)
        {
            //configuration = Locator.Current.GetService<IConfiguration>(); после этого не находит settings, "Object reference not set to an instance of an object."
            configuration = Configuration;
            settings = configuration.GetSection("ServiceStackSettings").Get<ServiceStackSettings>();

            try
            {
                var serviceStackKey = settings.LicenseKey;
                Licensing.RegisterLicense(serviceStackKey);
            }

            catch (LicenseException)
            {
                var licenseKeyAddress = settings.LicenseKeyAddress;
                var newTrialKey = GetNewTrialKeyFromHtmlText(licenseKeyAddress);

                settings.LicenseKey = newTrialKey;

                configuration.GetSection("ServiceStackSettings").Set(settings);

                var newServiceStackKey = settings.LicenseKey;

                Licensing.RegisterLicense(newServiceStackKey);
            }
        }

        private string GetNewTrialKeyFromHtmlText(string url)
        {
            string newTrialKey = string.Empty;
            string htmlText = new HttpClient().GetStringAsync(url).Result;
            int newKeyFirstIndex = htmlText.IndexOf("TRIAL");
            newTrialKey = htmlText.Substring(newKeyFirstIndex, 383);
            return newTrialKey;
        }

    }
}
