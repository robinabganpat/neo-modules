using Microsoft.Extensions.Configuration;
using System;
using System.Linq;

namespace Neo.Plugins
{
    class HttpsSettings
    {
        public TimeSpan Timeout { get; }

        public HttpsSettings(IConfigurationSection section)
        {
            Timeout = TimeSpan.FromMilliseconds(section.GetValue("Timeout", 5000));
        }
    }

    class Settings
    {
        public string[] Nodes { get; }
        public string Wallet { get; }
        public TimeSpan MaxTaskTimeout { get; }
        public bool AllowPrivateHost { get; }
        public string[] AllowedContentTypes { get; }
        public HttpsSettings Https { get; }

        public static Settings Default { get; private set; }

        private Settings(IConfigurationSection section)
        {
            Nodes = section.GetSection("Nodes").GetChildren().Select(p => p.Get<string>()).ToArray();
            Wallet = section.GetValue<string>("Wallet");
            MaxTaskTimeout = TimeSpan.FromMilliseconds(section.GetValue("MaxTaskTimeout", 432000000));
            AllowPrivateHost = section.GetValue("AllowPrivateHost", false);
            AllowedContentTypes = section.GetSection("AllowedContentTypes").GetChildren().Select(p => p.Get<string>()).ToArray();
            Https = new HttpsSettings(section.GetSection("Https"));
        }

        public static void Load(IConfigurationSection section)
        {
            Default = new Settings(section);
        }
    }
}
