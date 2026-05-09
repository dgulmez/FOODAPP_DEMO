using FoodApp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FoodApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== AKSA Food App — Rezervasyon Örneği ===\n");

            var state    = new AppState();
            var tokenPrv = new TokenProviderClient(Config.TokenProviderUrl);

            // ─────────────────────────────────────────────────────────────
            // ADIM 1: Kullanıcı Girişi
            //
            // TokenProvider'a kullanıcı adı ve şifre ile POST atılır.
            // Başarılı girişin ardından TokenProvider:
            //   - Praxapp Gateway'e logon isteği gönderir ve kimliği doğrular.
            //   - Kullanıcıya ait oturum verisini sunucu belleğinde saklar.
            //   - Oturumu temsil eden benzersiz bir sessionKey döndürür.
            //   - sessionKey 1 saat geçerlidir. Sonraki her başarılı token isteğinde bu 1 saat yeniden başlar (Sliding Expiration)
            //
            // sessionKey, sonraki tüm adımlarda tek seferlik Praxapp token'ı
            // üretmek için kullanılır. Ham şifre bir daha iletilmez.
            //
            // semiSecure=true: Kullanıcı kimlik doğrulamasını LDAP üzerinden
            // yapan sistemler için gereklidir. Yemek Portali kullanıcıları LDAP
            // kullanıcıları olduğu için hep "true" olarak gönderilmelidir.
            // ─────────────────────────────────────────────────────────────
            Console.Write("Kullanıcı adı: ");
            string username = Console.ReadLine().Trim();
            Console.Write("Şifre: ");
            string password = ReadPassword();

            Console.WriteLine("\nGiriş yapılıyor...");
            try
            {
                var loginResult = await tokenPrv.LoginAsync(username, password, semiSecure: true);
                state.SessionKey = loginResult.Item1;
                state.UserData   = loginResult.Item2;

                // Praxapp, kullanıcı bilgilerini "data" alanında döndürür.
                // userId sonraki adımlarda rezervasyon sorgusunda filtre olarak kullanılır.
                state.UserId = state.UserData["userId"] != null ? state.UserData["userId"].ToString() : null;

                Console.WriteLine(string.Format("\n[OK] SessionKey : {0}", state.SessionKey));
                Console.WriteLine(string.Format("     UserId     : {0}", state.UserId ?? "(belirlenemedi)"));
                Console.WriteLine(string.Format("\nKullanıcı verisi:\n{0}", JsonConvert.SerializeObject(state.UserData, Formatting.Indented)));
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("\n[HATA] {0}", ex.Message));
                return;
            }

            // PraxappClient; her API çağrısından önce otomatik olarak TokenProvider'dan
            // tek seferlik bir Praxapp token'ı üretir ve isteğe ekler.
            var praxapp = new PraxappClient(tokenPrv, state.SessionKey);

            // ─────────────────────────────────────────────────────────────
            // ADIM 2: Menü Tipleri (OptionTypeModel)
            //
            // Sistemdeki tüm menü tipi tanımlarını getirir.
            // Örnek tipler: Traditional (normal restoran), Bowl, FastFood.
            // Bu liste statik niteliktedir; nadiren değişir.
            // Menü kartlarını (SeatOption) listelemek ve sınıflandırmak için
            // referans veri olarak hafızada tutulabilir.
            // ─────────────────────────────────────────────────────────────
            Console.WriteLine("\nAdım 2: Menü tipleri alınıyor (OptionTypeModel)...");
            try
            {
                var result = await praxapp.QueryAsync("queryOptionTypeModel");
                foreach (var item in result["All"])
                    state.OptionTypeModels.Add(item);

                Console.WriteLine(string.Format("[OK] {0} menü tipi:", state.OptionTypeModels.Count));
                foreach (var m in state.OptionTypeModels)
                    Console.WriteLine(string.Format("     [{0}] {1}", m["ID"], m["OptionType"]["Text"]));
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("[HATA] {0}", ex.Message));
                return;
            }

            // ─────────────────────────────────────────────────────────────
            // ADIM 3: Zaman Dilimleri (TimeInterval)
            //
            // Her yemekhane (Seat) için tanımlanmış zaman aralıklarını getirir.
            // Her kayıt; hangi yemekhanenin, hangi saat aralığında kaç kişilik kapasiteye 
            // sahip olduğunu tanımlar.
            // Kapasite hesabı Adım 8'de bu verilerden yararlanılarak yapılır.
            // ─────────────────────────────────────────────────────────────
            Console.WriteLine("\nAdım 3: Zaman dilimleri alınıyor (TimeInterval)...");
            try
            {
                var result = await praxapp.QueryAsync("queryTimeInterval");
                foreach (var item in result["All"])
                    state.TimeIntervals.Add(item);

                Console.WriteLine(string.Format("[OK] {0} zaman dilimi:", state.TimeIntervals.Count));
                foreach (var t in state.TimeIntervals)
                    Console.WriteLine(string.Format("     [{0}] {1} — {2}-{3} (Kapasite: {4})",
                        t["ID"], t["Seat"]["Key"], t["StartTime"], t["EndTime"], t["Capacity"]));
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("[HATA] {0}", ex.Message));
                return;
            }

            // ─────────────────────────────────────────────────────────────
            // ADIM 4: Menü Tanımları (SeatOption)
            //
            // Belirli bir tarih aralığı için tanımlanmış tüm günlük menüleri getirir.
            // Her SeatOption; hangi yemekhanenin, hangi gün, hangi tür menü
            // sunduğunu ve menü içeriğini (Description) tanımlar.
            //
            // Aşağıdaki örnek sorgu içinde bulunulan haftanın başlangıcı (Pazartesi) gününden sonrası
            // için tanımlı olan menüleri getirir.
            // Praxapp'ta Date (sadece tarih, zaman yok) tipindeki alanlar veritabanında UTC gece yarısı
            // (00:00:00+00) olarak tutulur. Pazartesi tarihini filtrelemek için
            // tarihi örneğin "2026-05-12T00:00:00.000Z" formatında göndermek gerekir.
            // ─────────────────────────────────────────────────────────────
            Console.WriteLine("\nAdım 4: Menü kartları alınıyor (SeatOption)...");
            try
            {
                // İçinde bulunulan haftanın pazartesi günü bulunuyor.
                DateTime today = DateTime.Today;
                int daysFromMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                DateTime monday = today.AddDays(-daysFromMonday);

                string mondayParam = monday.ToString("yyyy-MM-dd") + "T00:00:00.000Z";
                Console.WriteLine(string.Format("     Başlangıç filtresi: {0}", mondayParam));

                // Praxapp standart apilerine sorgu atarken hazırlanacak olan "query" parametresi, herhangi bir SQL tabloya
                // WHERE koşulu ekler gibi eklenebilir. Bu örnekte >= @date kullanıldı fakat sadece belli bir tarihte tanımlı menüler 
                // alınmak istenirse "Date = @date" şeklinde bir query oluşturulmalıdır. Bir tarih aralığındaki kayıtlar alınmak istenirse
                // "Date >= @startDate AND Date <= @endDate" şeklinde oluşturulur. 
                // UYARI: Query sorgusu boş gönderilirse tablodaki tüm kayıtlar döner. 
                var result = await praxapp.QueryAsync(
                    "querySeatOption",
                    query: "Date >= @date",
                    args: new object[] { mondayParam });

                foreach (var item in result["All"])
                    state.SeatOptions.Add(item);

                Console.WriteLine(string.Format("[OK] {0} menü kartı alındı.", state.SeatOptions.Count));
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("[HATA] {0}", ex.Message));
                return;
            }

            // ─────────────────────────────────────────────────────────────
            // ADIM 5: Kullanıcının Mevcut Rezervasyonları (Reservation)
            //
            // Giriş yapmış kullanıcının bu haftadan itibaren yaptığı tüm
            // rezervasyonları getirir.
            //
            // Bir rezervasyon kaydının iki farklı anlamı olabilir:
            //   - SeatOption ve TimeInterval doluysa: Kullanıcı belirli bir
            //     menüyü belirli bir zaman aralığı için rezerve etmiştir.
            //   - SeatOption ve TimeInterval null ise: Kullanıcı o gün için
            //     "Yemek istemiyorum" seçimini yapmıştır. Bu bilgi sistemde
            //     bilinçli olarak saklanır; ilgili günün menüleri bu durumu
            //     yansıtacak şekilde işaretlenir.
            //
            // Sorgu parametreleri: @p0 = tarih filtresi, @p2 = kullanıcı ID'si.
            // ─────────────────────────────────────────────────────────────
            Console.WriteLine("\nAdım 5: Kullanıcının rezervasyonları alınıyor...");
            try
            {
                DateTime today = DateTime.Today;
                int daysFromMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                DateTime monday = today.AddDays(-daysFromMonday);
                string mondayParam = monday.ToString("yyyy-MM-dd") + "T00:00:00.000Z";

                var result = await praxapp.QueryAsync(
                    "queryReservation",
                    query: "Date >= @p0 AND Person.Account.ID = @p2",
                    args: new object[] { mondayParam, state.UserId });

                foreach (var item in result["All"])
                    state.Reservations.Add(item);

                Console.WriteLine(string.Format("[OK] {0} rezervasyon alındı.", state.Reservations.Count));
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("[HATA] {0}", ex.Message));
                return;
            }

            // ─────────────────────────────────────────────────────────────
            // ADIM 6-9: Tarih Seçimi → Menü → Zaman Dilimi → Rezervasyon
            //
            // Tarih listesinde her tarihin altında o güne ait menüler ve
            // mevcut rezervasyon durumu gösterilir. Kullanıcı tarih seçtikten
            // sonra menü (veya Yemek istemiyorum) ve zaman dilimi seçer.
            // Seçilen tarih için rezervasyon varsa update, yoksa create çağrılır.
            // Her işlem sonrası rezervasyonlar yenilenir ve tarih listesi yeniden
            // gösterilir. Çıkmak için Q kullanılır.
            // ─────────────────────────────────────────────────────────────
            var trCulture = new CultureInfo("tr-TR");
            var sortedOptions = state.SeatOptions
                .OrderBy(s => (DateTime)s["Date"])
                .ThenBy(s => s["Seat"]["Key"].ToString())
                .ToList();

            var dates = sortedOptions
                .Select(s => ((DateTime)s["Date"]).Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            while (true)
            {
                // Rezervasyon durumunu her turda güncel state'den hesapla
                var resByOptionId = state.Reservations
                    .Where(r => r["SeatOption"] != null && r["SeatOption"].Type != JTokenType.Null)
                    .ToDictionary(r => r["SeatOption"]["ID"].ToString());

                var resByDate = state.Reservations
                    .Where(r => r["SeatOption"] == null || r["SeatOption"].Type == JTokenType.Null)
                    .ToDictionary(r => ((DateTime)r["Date"]).Date);

                // ── ADIM 6: Tarih listesi ──
                Console.WriteLine("\n" + new string('═', 70));
                for (int d = 0; d < dates.Count; d++)
                {
                    DateTime date = dates[d];
                    Console.WriteLine(string.Format("\n  {0}.  {1}",
                        d + 1, date.ToString("dd.MM.yyyy ddd", trCulture)));

                    var optsForDate = sortedOptions
                        .Where(s => ((DateTime)s["Date"]).Date == date)
                        .ToList();

                    string yiRes = resByDate.ContainsKey(date) ? "[Yemek İstemiyorum Seçilmiş]" : "";
                    Console.WriteLine(string.Format("       {0,-12} {1,-22} {2}",
                        "-", "Yemek istemiyorum", yiRes));

                    foreach (var opt in optsForDate)
                    {
                        string seat    = opt["Seat"]["Key"].ToString();
                        string optType = opt["OptionType"]["Value"].ToString();
                        string resInfo = "";

                        if (resByOptionId.ContainsKey(opt["ID"].ToString()))
                        {
                            var res = resByOptionId[opt["ID"].ToString()];
                            if (res["TimeInterval"] != null && res["TimeInterval"].Type != JTokenType.Null)
                            {
                                string tiId = res["TimeInterval"]["ID"].ToString();
                                var ti = state.TimeIntervals.FirstOrDefault(t => t["ID"].ToString() == tiId);
                                resInfo = ti != null
                                    ? string.Format("[{0}-{1}]", ti["StartTime"], ti["EndTime"])
                                    : "[Rezervasyon var]";
                            }
                            else
                            {
                                resInfo = "[Rezervasyon var]";
                            }
                        }

                        Console.WriteLine(string.Format("       {0,-12} {1,-22} {2}", seat, optType, resInfo));
                    }
                }

                Console.WriteLine("\n" + new string('═', 70));
                Console.Write("Tarih seçin (numara, çıkmak için Q): ");

                string dateInput = Console.ReadLine().Trim();
                if (string.Equals(dateInput, "Q", StringComparison.OrdinalIgnoreCase))
                    return;

                if (!int.TryParse(dateInput, out int dateChoice) || dateChoice < 1 || dateChoice > dates.Count)
                {
                    Console.WriteLine("[HATA] Geçersiz seçim.");
                    continue;
                }

                DateTime selectedDate = dates[dateChoice - 1];
                var optionsForDate = sortedOptions
                    .Where(s => ((DateTime)s["Date"]).Date == selectedDate)
                    .OrderBy(s => s["Seat"]["Key"].ToString())
                    .ToList();

                // Seçilen tarihe ait mevcut rezervasyon — create/update kararı için
                JToken existingRes = state.Reservations
                    .FirstOrDefault(r => ((DateTime)r["Date"]).Date == selectedDate);

                // ── ADIM 7: Menü seçimi ──
                Console.WriteLine(string.Format("\n{0}:",
                    selectedDate.ToString("dd.MM.yyyy ddd", trCulture)));
                Console.WriteLine(new string('─', 62));
                Console.WriteLine(string.Format(" {0,-4} {1,-12} {2,-22} {3}", "#", "Yemekhane", "Menü Tipi", "Rezervasyon"));
                Console.WriteLine(new string('─', 62));

                string yiMenuRes = resByDate.ContainsKey(selectedDate) ? "[Rezervasyon var]" : "";
                Console.WriteLine(string.Format(" {0,-4} {1,-12} {2,-22} {3}", "0", "-", "Yemek istemiyorum", yiMenuRes));

                for (int i = 0; i < optionsForDate.Count; i++)
                {
                    var opt    = optionsForDate[i];
                    string seat    = opt["Seat"]["Key"].ToString();
                    string optType = opt["OptionType"]["Value"].ToString();
                    string resInfo = "";

                    if (resByOptionId.ContainsKey(opt["ID"].ToString()))
                    {
                        var res = resByOptionId[opt["ID"].ToString()];
                        if (res["TimeInterval"] != null && res["TimeInterval"].Type != JTokenType.Null)
                        {
                            string tiId = res["TimeInterval"]["ID"].ToString();
                            var ti = state.TimeIntervals.FirstOrDefault(t => t["ID"].ToString() == tiId);
                            resInfo = ti != null
                                ? string.Format("[{0}-{1}]", ti["StartTime"], ti["EndTime"])
                                : "[Rezervasyon var]";
                        }
                        else
                        {
                            resInfo = "[Rezervasyon var]";
                        }
                    }

                    Console.WriteLine(string.Format(" {0,-4} {1,-12} {2,-22} {3}", i + 1, seat, optType, resInfo));
                }

                Console.WriteLine(new string('─', 62));
                Console.Write("Menü seçin (0 = Yemek istemiyorum, numara, geri için G): ");

                string menuInput = Console.ReadLine().Trim();
                if (string.Equals(menuInput, "G", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!int.TryParse(menuInput, out int menuChoice) || menuChoice < 0 || menuChoice > optionsForDate.Count)
                {
                    Console.WriteLine("[HATA] Geçersiz seçim.");
                    continue;
                }

                bool isYemekIstemiyorum = menuChoice == 0;
                JToken selectedOption   = isYemekIstemiyorum ? null : optionsForDate[menuChoice - 1];
                JToken selectedInterval = null;

                if (!isYemekIstemiyorum)
                {
                    // ── ADIM 8: Doluluk ve zaman dilimi ──
                    Console.WriteLine("\nAdım 8: Doluluk bilgisi alınıyor (GetCapacityInfo)...");
                    JToken capacityResult = null;
                    try
                    {
                        string seatId   = selectedOption["Seat"]["ID"].ToString();
                        string capParam = selectedDate.ToString("yyyy-MM-dd") + "T00:00:00.000Z";
                        capacityResult  = await praxapp.CallAsync("GetCapacityInfo", new { p1 = seatId, p2 = capParam });
                        Console.WriteLine("[OK] Doluluk verisi alındı.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(string.Format("[HATA] {0}", ex.Message));
                        continue;
                    }

                    string selectedSeatId = selectedOption["Seat"]["ID"].ToString();
                    var seatIntervals = state.TimeIntervals
                        .Where(t => t["Seat"]["ID"].ToString() == selectedSeatId)
                        .OrderBy(t => t["StartTime"].ToString())
                        .ToList();

                    var occupancy = new Dictionary<string, int>();
                    if (capacityResult != null && capacityResult.Type == JTokenType.Array)
                    {
                        foreach (var c in capacityResult)
                            occupancy[c["ID"].ToString()] = (int)c["Seats"];
                    }

                    Console.WriteLine("\n" + new string('─', 52));
                    Console.WriteLine(string.Format(" {0,-3} {1,-14} {2,-10} {3}", "#", "Zaman Dilimi", "Kapasite", "Müsait"));
                    Console.WriteLine(new string('─', 52));

                    for (int i = 0; i < seatIntervals.Count; i++)
                    {
                        var ti       = seatIntervals[i];
                        int capacity = (int)ti["Capacity"];
                        int occupied = occupancy.ContainsKey(ti["ID"].ToString()) ? occupancy[ti["ID"].ToString()] : 0;
                        Console.WriteLine(string.Format(" {0,-3} {1}-{2}       {3,-10} {4}",
                            i + 1, ti["StartTime"], ti["EndTime"], capacity, capacity - occupied));
                    }

                    Console.WriteLine(new string('─', 52));
                    Console.Write("Zaman dilimi seçin (numara, geri için G): ");

                    string tiInput = Console.ReadLine().Trim();
                    if (string.Equals(tiInput, "G", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!int.TryParse(tiInput, out int tiChoice) || tiChoice < 1 || tiChoice > seatIntervals.Count)
                    {
                        Console.WriteLine("[HATA] Geçersiz seçim.");
                        continue;
                    }

                    selectedInterval = seatIntervals[tiChoice - 1];
                    Console.WriteLine(string.Format("Seçilen: {0}-{1}",
                        selectedInterval["StartTime"], selectedInterval["EndTime"]));
                }

                // ── ADIM 9: Rezervasyon oluştur veya güncelle ──
                // Seçilen tarihte mevcut rezervasyon varsa update, yoksa create.
                bool isUpdate = existingRes != null;
                Console.WriteLine(string.Format("\nAdım 9: Rezervasyon {0}...", isUpdate ? "güncelleniyor" : "oluşturuluyor"));
                try
                {
                    JToken result;
                    if (isUpdate)
                    {
                        result = await praxapp.CallAsync("updateReservation", new
                        {
                            EID = existingRes["ID"].ToString(),
                            e   = new
                            {
                                SeatOption   = isYemekIstemiyorum ? (string)null : selectedOption["ID"].ToString(),
                                TimeInterval = isYemekIstemiyorum ? (string)null : selectedInterval["ID"].ToString()
                            }
                        });
                    }
                    else
                    {
                        string dateParam = selectedDate.ToString("yyyy-MM-dd") + "T00:00:00Z";
                        result = await praxapp.CallAsync("createReservation", new
                        {
                            e = new
                            {
                                Date         = dateParam,
                                SeatOption   = isYemekIstemiyorum ? (string)null : selectedOption["ID"].ToString(),
                                TimeInterval = isYemekIstemiyorum ? (string)null : selectedInterval["ID"].ToString()
                            }
                        });
                    }

                    bool hasErrors = result["HasErrors"] != null && (bool)result["HasErrors"];
                    if (hasErrors)
                    {
                        string errMsg = result["Messages"] != null && result["Messages"].HasValues
                            ? result["Messages"][0]["Content"].ToString()
                            : "Bilinmeyen hata.";
                        Console.WriteLine(string.Format("[HATA] {0}", errMsg));
                    }
                    else
                    {
                        Console.WriteLine(string.Format("[OK] Rezervasyon başarıyla {0}.",
                            isUpdate ? "güncellendi" : "oluşturuldu"));
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format("[HATA] {0}", ex.Message));
                }

                // Rezervasyon listesini API'den yenile (başarı veya hata sonrası)
                try
                {
                    DateTime today = DateTime.Today;
                    int daysFromMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                    DateTime monday = today.AddDays(-daysFromMonday);
                    string mondayParam = monday.ToString("yyyy-MM-dd") + "T00:00:00.000Z";

                    var refreshResult = await praxapp.QueryAsync(
                        "queryReservation",
                        query: "Date >= @p0 AND Person.Account.ID = @p2",
                        args: new object[] { mondayParam, state.UserId });

                    state.Reservations.Clear();
                    foreach (var r in refreshResult["All"])
                        state.Reservations.Add(r);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format("[UYARI] Rezervasyonlar yenilenirken hata: {0}", ex.Message));
                }
            }
        }

        // Şifre girişinde karakterleri ekranda gizler (üretim kullanımı için).
        // Test sırasında Console.ReadLine() ile değiştirilebilir.
        static string ReadPassword()
        {
            ConsoleColor prev = Console.ForegroundColor;
            Console.ForegroundColor = Console.BackgroundColor;
            string pwd = Console.ReadLine() ?? string.Empty;
            Console.ForegroundColor = prev;
            return pwd;
        }
    }
}
