# FoodApp — Praxapp Food API Geliştirici Kılavuzu

`FoodApp`, **PraxappTokenProvider** üzerinden kimlik doğrulama yaparak Praxapp Food API'sini kullanan bir referans uygulamasıdır.

---

## Genel Mimari

```
İstemci Uygulama
     │  1) login (kullanıcı adı + şifre)
     ▼
PraxappTokenProvider  (IIS / ASP.NET — TokenService.ashx)
     │  2) logon → Praxapp Gateway
     │  3) getToken (her API çağrısı öncesi)
     ▼
Praxapp Gateway  (?appId=...&binding=json&op=...)
```

İstemci şifreyi yalnızca login sırasında TokenProvider'a gönderir. Sonraki tüm Gateway çağrılarında `sessionKey` üzerinden tek seferlik token alınır.

---

## TokenProvider

**URL:** `http://<sunucu>/PraxappTokenProvider/TokenService.ashx`  
**Metod:** POST — `application/x-www-form-urlencoded`  
**Zorunlu başlıklar:** `Authorization: Basic <base64(user:pass)>`

Praxapp, token üretiminde `clientIP` ve `User-Agent` değerlerini HMAC hesabına katar. TokenProvider ayrıca `getToken` çağrısında IP doğrulaması yapar. Bu nedenle tek kural şudur: **`login`, `getToken` ve Gateway isteğinde gönderilen `clientIP` / `User-Agent` değerleri birebir aynı olmalıdır.** Uyuşmazlık token reddine yol açar.

Backend bir uygulama bu değerleri iki farklı şekilde yönetebilir:

- **Backend kendi bilgilerini kullanır:** `clientIP` olarak sunucunun IP'sini, `User-Agent` olarak sabit bir string gönderir. Basit ve yönetimi kolaydır.
- **Client bilgileri iletilir:** `clientIP` ve `User-Agent` son kullanıcıdan alınıp iletilir. TokenProvider'ın IP doğrulaması sayesinde oturum o kullanıcıya bağlanır; ek bir session güvenliği katmanı oluşturur.

### action=login

| Parametre | Açıklama |
|---|---|
| `action` | `login` |
| `apiKey` | Praxapp uygulama anahtarı |
| `username` | Kullanıcı adı |
| `password` | Ham şifre |
| `clientIP` | Sunucunun kendi IP'si veya son kullanıcının IP'si (bkz. yukarıdaki not) |
| `semiSecure` | `true` (LDAP kullanıcıları için zorunlu) |

**Yanıt (200):**
```json
{ "success": true, "sessionKey": "...", "data": { "userId": "0000000000P7", "fullName": "..." } }
```

`sessionKey` → token taleplerinde kullanılır.  
`data.userId` → `queryReservation` filtresinde `Person.Account.ID` olarak kullanılır.

| HTTP | Anlamı |
|---|---|
| 401 | Hatalı kimlik bilgisi |
| 403 | IP uyuşmazlığı |
| 504 | Gateway zaman aşımı |

### action=getToken

| Parametre | Açıklama |
|---|---|
| `action` | `getToken` |
| `apiKey` | Praxapp uygulama anahtarı |
| `key` | Login'den alınan `sessionKey` |
| `clientIP` | Login'deki `clientIP` ile aynı değer |

**Yanıt (200, text/plain):** Token string'i — her Gateway çağrısından önce alınmalıdır, tek seferlik kullanım.

---

## Praxapp Gateway İsteği

```
POST https://<gateway>/?appId=<apiKey>&binding=json&op=<operasyon>
Authorization: Basic <base64(user:pass)>
X-Forwarded-For: <son kullanıcının IP adresi — login/getToken'daki clientIP ile aynı>
User-Agent: <son kullanıcının User-Agent'ı — login/getToken ile aynı>
Content-Type: application/json

{ "token": "...", ...operasyon parametreleri... }
```

Referans implementasyon: `PraxappClient.cs → CallAsync`

---

## Varlık Modeli

### OptionTypeModel — Menü Tipi

Statik referans veridir; nadiren değişir.

| Alan | Tür | Açıklama |
|---|---|---|
| `ID` | string | Nesne kimliği |
| `OptionType.Text` | string | Görüntü adı (ör. "Plus Kitchen Restaurant") |
| `OptionType.Value` | string | Kod adı (ör. `Traditional`, `Bowl`) |
| `Photo.FileName` | string | Fotoğraf dosya adı |
| `Photo.PublicLink` | string | Fotoğraf erişim anahtarı |
| `Position` | int | Listeleme sırası |

### Seat — Yemekhane

Bağımsız sorgulanmaz; `TimeInterval` ve `SeatOption` içinde gömülü `Entity` olarak gelir.

| Alan | Tür |
|---|---|
| `ID` | string |
| `Key` | string |
| `Address` | string |

### TimeInterval — Zaman Dilimi

| Alan | Tür | Açıklama |
|---|---|---|
| `ID` | string | |
| `Key` | string | |
| `Seat` | Entity | Bağlı yemekhane |
| `StartTime` | string | `HH:mm` |
| `EndTime` | string | `HH:mm` |
| `Capacity` | int | Maksimum kişi sayısı |

### SeatOption — Günlük Menü

| Alan | Tür | Açıklama |
|---|---|---|
| `ID` | string | |
| `Key` | string | |
| `Date` | dateTime | Menü tarihi — API'den `T03:00:00+03:00` formatında gelir |
| `Description` | string | Menü içeriği (`\r\n` ile ayrılmış satırlar) |
| `OptionType` | PickVal | `Text` + `Value` |
| `Seat` | Entity | Ait olduğu yemekhane |
| `CreatedBy` / `ModifiedBy` | User | `Name`, `FullName`, `LastLogonDate` |
| `CreatedOn` / `ModifiedOn` | dateTime | |

### Reservation — Rezervasyon

| Alan | Tür | Açıklama |
|---|---|---|
| `ID` | string | |
| `Key` | string | |
| `Date` | dateTime | API'den `T03:00:00+03:00` formatında gelir |
| `SeatOption` | Entity\|null | null ise "Yemek istemiyorum" |
| `TimeInterval` | Entity\|null | SeatOption null ise bu da null |
| `Person` | Entity | Rezervasyonu yapan kişi |

> `Person.ID` nesne kimliğidir. `queryReservation` filtresinde kullanılan `Person.Account.ID`, login yanıtındaki `data.userId` değeridir; ikisi farklıdır.

**Rezervasyon türleri:**

| SeatOption | TimeInterval | Anlam |
|---|---|---|
| Dolu | Dolu | Menü + saat rezervasyonu |
| null | null | "Yemek istemiyorum" |

### Entity (gömülü referans tipi)

`Seat`, `SeatOption`, `TimeInterval`, `Person` nesneleri sorgu yanıtlarında `Entity` olarak gömülü gelir:

| Alan | Tür |
|---|---|
| `ID` | string |
| `Key` | string |
| `MetaEntityID` | string\|null |

---

## Operasyonlar

### Sorgu yapısı

```json
{ "token": "...", "query": "Alan >= @baslangic AND Diger = @kullanici", "args": ["deger0", "deger1"] }
```

Parametre adları (`@baslangic`, `@kullanici`, `@p0`…) anlamsızdır; Praxapp bunları query string içinde **soldan sağa görünüm sırasına** göre `args` dizisiyle eşleştirir. İlk parametre adı → `args[0]`, ikincisi → `args[1]`.

**Genel yanıt yapısı:**
```json
{ "HasErrors": false, "All": [ ... ], "Messages": [] }
```

---

### queryOptionTypeModel

Tüm menü tipi tanımlarını getirir. `query` ve `args` boş gönderilebilir.

### queryTimeInterval

Tüm yemekhanelerin saat dilimlerini getirir. `query` ve `args` boş gönderilebilir.

### querySeatOption

Belirli bir tarihten itibaren tanımlı günlük menüleri getirir.

```json
{ "token": "...", "query": "Date >= @p0", "args": ["2026-05-12T00:00:00.000Z"] }
```

### queryReservation

Kullanıcının rezervasyonlarını getirir.

```json
{
  "token": "...",
  "query": "Date >= @baslangic AND Person.Account.ID = @kullanici",
  "args": ["2026-05-12T00:00:00.000Z", "0000000000P7"]
}
```

`args[0]` → query'deki ilk parametre (`@baslangic`), `args[1]` → ikinci parametre (`@kullanici`).

> **Uyarı:** `query` parametresi boş string (`""`) olarak gönderilirse Praxapp ilgili tablodaki tüm kayıtları döndürür. `queryReservation` gibi kullanıcı bazlı filtreleme gerektiren sorgularda `query` alanının her zaman doldurulması zorunludur.

### GetCapacityInfo

Seçilen yemekhane ve tarih için her zaman dilimindeki anlık doluluk sayısını döndürür.

```json
{ "token": "...", "p1": "<Seat.ID>", "p2": "2026-05-15T00:00:00.000Z" }
```

**Yanıt (array):**
```json
[{ "ID": "<TimeInterval.ID>", "Seats": 47 }, ...]
```

`Seats` = o dilimde mevcut rezervasyon sayısı. Yanıtta bulunmayan zaman dilimleri için doluluk sıfırdır.  
**Müsait:** `TimeInterval.Capacity - Seats`

### createReservation

```json
{
  "token": "...",
  "e": {
    "Date":         "2026-05-15T00:00:00Z",
    "SeatOption":   "<SeatOption.ID>",
    "TimeInterval": "<TimeInterval.ID>"
  }
}
```

| Alan | Zorunlu | Açıklama |
|---|---|---|
| `Date` | Evet | UTC gece yarısı — `yyyy-MM-ddT00:00:00Z` |
| `SeatOption` | Hayır | null → "Yemek istemiyorum" |
| `TimeInterval` | Hayır | null → "Yemek istemiyorum" |
| `Person` | Hayır | null → token sahibi kullanıcı |

**Başarılı yanıt:** `{ "HasErrors": false, "Messages": [] }`

**Hata yanıtı:**
```json
{ "HasErrors": true, "Messages": [{ "IsError": true, "Content": "Mükerrer kayıt..." }] }
```

Aynı kullanıcı + aynı gün için ikinci rezervasyon oluşturulmaya çalışılırsa `HasErrors: true` döner.

### updateReservation

Mevcut rezervasyonun menüsünü veya zaman dilimini değiştirir. `e` içine yalnızca değiştirilecek alanlar gönderilir.

```json
{
  "token": "...",
  "EID": "<Reservation.ID>",
  "e": {
    "SeatOption":   "<SeatOption.ID>",
    "TimeInterval": "<TimeInterval.ID>"
  }
}
```

`SeatOption` ve `TimeInterval` null gönderilirse "Yemek istemiyorum" olarak güncellenir.  
Başarılı ve hata yanıtı `createReservation` ile aynı yapıdadır.

---

## Rezervasyon Akışı

```
1. login              → sessionKey + userId
2. queryOptionTypeModel → Menü tipleri (statik, bir kez alınır)
3. queryTimeInterval  → Yemekhane saat dilimleri ve kapasiteler (statik)
4. querySeatOption    → Bu haftanın günlük menüleri
5. queryReservation   → Kullanıcının mevcut rezervasyonları

─── Etkileşimli döngü ───────────────────────────────────────────
6. Tarih seçimi       → SeatOption'lar ve mevcut rezervasyon durumu gösterilir
7. Menü seçimi        → 0 = Yemek istemiyorum
8. GetCapacityInfo    → Seçilen yemekhane + tarih için anlık doluluk
9. TimeInterval seçimi → Müsait = Kapasite - Doluluk
10. createReservation  → Seçilen tarihte rezervasyon yoksa
    updateReservation  → Seçilen tarihte rezervasyon varsa (EID otomatik belirlenir)
11. queryReservation   → Rezervasyonlar yenilenir → 6. adıma dön
```

---

## Önemli Notlar

### Tarih formatları

Praxapp, `Date` tipini kavramsal olarak takvim günü olarak kullanır; ancak veritabanında UTC olarak saklar.

| Bağlam | Format |
|---|---|
| Sorgu filtresi (`querySeatOption`, `queryReservation`) | `yyyy-MM-ddT00:00:00.000Z` |
| `GetCapacityInfo p2` | `yyyy-MM-ddT00:00:00.000Z` |
| `createReservation Date` | `yyyy-MM-ddT00:00:00Z` |
| API yanıtlarında gelen `Date` | `yyyy-MM-ddT03:00:00+03:00` |

`createReservation`'da `T00:00:00+03:00` gönderilirse sunucuda bir önceki güne kaydedilir; `T00:00:00Z` kullanılmalıdır.

### clientIP belirleme

Backend uygulamalar genellikle kendi IP adreslerini kullanır. `FoodApp` gibi doğrudan çalışan uygulamalarda ağ arayüzü şöyle tespit edilebilir:

```csharp
using (var socket = new UdpClient())
{
    socket.Connect("praxappqa.aksa.com.tr", 443);
    string ip = ((IPEndPoint)socket.Client.LocalEndPoint).Address.ToString();
}
```

Bu yöntem paket göndermez; yalnızca hangi ağ arabiriminin kullanıldığını belirler.

### Ortam yapılandırması

| Ortam | Gateway URL |
|---|---|
| QA | `https://praxappqa.aksa.com.tr/gateway` |
| Production | `https://praxapp.aksa.com.tr/gateway` |

`Config.cs → GatewayUrl` ve `TokenProviderClient.GetLocalIp()` içindeki host adresi production geçişinde güncellenmelidir.
