using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace FoodApp
{
    class AppState
    {
        public string  SessionKey { get; set; }
        public string  UserId     { get; set; }
        public JToken  UserData   { get; set; }

        public List<JToken> OptionTypeModels { get; } = new List<JToken>();
        public List<JToken> TimeIntervals    { get; } = new List<JToken>();
        public List<JToken> SeatOptions      { get; } = new List<JToken>();
        public List<JToken> Reservations     { get; } = new List<JToken>();
    }
}
