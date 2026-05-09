namespace FoodApp
{
    static class Config
    {
        //public const string TokenProviderUrl = "http://localhost/PraxappTokenProvider/TokenService.ashx";
        public const string TokenProviderUrl = "https://praxappqa.aksa.com.tr/tokenProvider/TokenService.ashx";
        public const string GatewayUrl       = "https://praxappqa.aksa.com.tr/gateway";

        public const string AuthUser  = "aksa";
        public const string AuthPass  = "aksa123";
        public const string ApiKey    = "0000000000Mb";
        public const string UserAgent = "FoodApp/1.0";
    }
}
