using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace FoodApp
{
    class PraxappClient
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly string _authHeader;
        private readonly TokenProviderClient _tokenProvider;
        private readonly string _sessionKey;

        public PraxappClient(TokenProviderClient tokenProvider, string sessionKey)
        {
            _authHeader    = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Config.AuthUser + ":" + Config.AuthPass));
            _tokenProvider = tokenProvider;
            _sessionKey    = sessionKey;
        }

        public async Task<JToken> CallAsync(string op, object payload)
        {
            string token = await _tokenProvider.GetTokenAsync(_sessionKey);

            var body = JObject.FromObject(payload);
            body["token"] = token;

            string url = string.Format("{0}/?appId={1}&binding=json&op={2}", Config.GatewayUrl, Config.ApiKey, op);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", _authHeader);
            request.Headers.Add("X-Forwarded-For", _tokenProvider.ClientIp);
            request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception(string.Format("Praxapp {0} hatası ({1}): {2}", op, (int)response.StatusCode, responseBody));

            var parsed = JToken.Parse(responseBody);
            if (parsed is JValue jval)
                throw new Exception(string.Format("Praxapp {0}: {1}", op, jval.ToString()));
            if (parsed is JObject parsedObj && parsedObj["HasErrors"]?.Value<bool>() == true)
            {
                string msg = parsedObj["Messages"]?[0]?["Content"]?.ToString() ?? "Gateway hatası.";
                throw new Exception(string.Format("Praxapp {0}: {1}", op, msg));
            }
            return parsed;
        }

        public Task<JToken> QueryAsync(string op, string query = "", object[] args = null)
        {
            return CallAsync(op, new { args = args ?? new object[0], query });
        }
    }
}
