using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FoodApp
{
    class TokenProviderClient
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly string _baseUrl;
        private string _clientIp;

        public TokenProviderClient(string baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public string ClientIp
        {
            get
            {
                if (_clientIp == null)
                    _clientIp = GetLocalIp();
                return _clientIp;
            }
        }

        public async Task<Tuple<string, JToken>> LoginAsync(string username, string password, bool semiSecure = false)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"]   = username,
                ["password"]   = password,
                ["clientIP"]   = ClientIp
            });

            string url = _baseUrl + (semiSecure ? "/ldaplogin" : "/securelogin");
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", MakeBasicAuth(Config.AuthUser, Config.AuthPass));
            request.Headers.TryAddWithoutValidation("apiKey", Config.ApiKey);
            request.Content = form;

            var response = await _http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            if (!response.IsSuccessStatusCode)
            {
                string msg = json["message"] != null ? json["message"].ToString() : body;
                throw new Exception(string.Format("Login başarısız ({0}): {1}", (int)response.StatusCode, msg));
            }

            return Tuple.Create(json["sessionKey"].ToString(), json["data"]);
        }

        public async Task<string> GetTokenAsync(string sessionKey)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["key"]      = sessionKey,
                ["clientIP"] = ClientIp
            });

            string url = _baseUrl + "/getToken";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", MakeBasicAuth(Config.AuthUser, Config.AuthPass));
            request.Headers.TryAddWithoutValidation("apiKey", Config.ApiKey);
            request.Content = form;

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new Exception(string.Format("getToken başarısız: {0}", response.StatusCode));

            return await response.Content.ReadAsStringAsync();
        }

        private static string MakeBasicAuth(string user, string pass)
        {
            return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + pass));
        }

        private static string GetLocalIp()
        {
            using (var socket = new UdpClient())
            {
                socket.Connect("praxappqa.aksa.com.tr", 443);
                return ((IPEndPoint)socket.Client.LocalEndPoint).Address.ToString();
            }
        }
    }
}
