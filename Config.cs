using Microsoft.Extensions.Configuration;

namespace FoodApp
{
    static class Config
    {
        public static readonly string TokenProviderUrl;
        public static readonly string GatewayUrl;
        public static readonly string AuthUser;
        public static readonly string AuthPass;
        public static readonly string ApiKey;
        public static readonly string UserAgent;

        static Config()
        {
            var cfg = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            TokenProviderUrl = cfg["TokenProviderUrl"];
            GatewayUrl       = cfg["GatewayUrl"];
            AuthUser         = cfg["AuthUser"];
            AuthPass         = cfg["AuthPass"];
            ApiKey           = cfg["ApiKey"];
            UserAgent        = cfg["UserAgent"];
        }
    }
}
