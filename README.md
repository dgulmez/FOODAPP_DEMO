# FoodApp — Praxapp Food API Geliştirici Kılavuzu

`FoodApp`, **PraxappTokenProvider.Core** üzerinden kimlik doğrulama yaparak Praxapp Food API'sini kullanan bir referans uygulamasıdır.

> Bu uygulama **tek bir API kullanıcısı** ile çalışır. Son kullanıcıdan kullanıcı adı / şifre **alınmaz**. Bunun yerine, işlem yapılacak personel ilk adımda girilen **sicil no** ile belirlenir. API kullanıcısı bilgileri `appsettings.json` içinde tutulur.

---

## Genel Mimari

```
İstemci Uygulama
     │  0) Personel sicil no'su alınır (UI)
     │  1) securelogin (API kullanıcısı ile — bir kez)
     ▼
PraxappTokenProvider.Core  (https://<sunucu>/api/token/...)
     │  2) logon → Praxapp Gateway
     │  3) getToken (her API çağrısı öncesi)
     ▼
Praxapp Gateway  (?appId=...&binding=json&op=...)
```

API kullanıcısının şifresi yalnızca login sırasında TokenProvider'a gönderilir. Sonraki tüm Gateway çağrılarında `sessionKey` üzerinden tek seferlik token alınır. İşlem yapılacak personel, sorgu ve rezervasyon operasyonlarına geçirilen **sicil no** ile belirlenir.

---

## Yapılandırma (`appsettings.json`)

| Anahtar | Açıklama |
|---|---|
| `TokenProviderUrl` | TokenProvider base URL — `https://<sunucu>/api/token` |
| `GatewayUrl` | Praxapp Gateway URL |
| `ApiKey` | Praxapp uygulama anahtarı (Diginity sağlar) — `apiKey` header'ı ve `appId` query parametresi olarak kullanılır |
| `AuthUser` / `AuthPass` | TokenProvider/Gateway erişimi için Basic auth bilgileri (Diginity sağlar) |
| `ApiUser` / `ApiPassword` | Tüm işlemlerin yapıldığı **tek Praxapp API kullanıcısı** — login'de `username`/`password` olarak gönderilir |

> `ApiUser`/`ApiPassword` ve `AuthUser`/`AuthPass` iki ayrı kimlik bilgisidir:
> - **Basic auth (`AuthUser`/`AuthPass`)** → TokenProvider ve Gateway'e erişim yetkisidir, HTTP `Authorization` header'ında gider.
> - **API kullanıcısı (`ApiUser`/`ApiPassword`)** → Praxapp tarafında oturum açan kullanıcıdır, login form body'sinde gider.

---

## TokenProvider

**Base URL:** `https://<sunucu>/api/token`
**Metod:** Tüm endpoint'ler `POST` — `application/x-www-form-urlencoded`
**Zorunlu başlıklar:**
- `Authorization: Basic <base64(AuthUser:AuthPass)>`
- `apiKey: <ApiKey>`

> Praxapp, token üretiminde `clientIP` değerini doğrulamaya katar. TokenProvider `getToken` çağrısında IP doğrulaması yapar. Tek kural: **`login` ve `getToken` çağrılarında gönderilen `clientIP` birebir aynı olmalıdır.** Uyuşmazlık `403` ile token reddine yol açar.

### POST /securelogin

Standart Praxapp kullanıcıları için. Bu uygulama API kullanıcısı ile `/securelogin` çağırır.

| Parametre | Açıklama |
|---|---|
| `username` | API kullanıcı adı (`ApiUser`) |
| `password` | API kullanıcı şifresi (`ApiPassword`) |
| `clientIP` | İsteği gönderen sunucunun IP'si veya son kullanıcının IP'si (bkz. clientIP belirleme) |

**Yanıt (200):**
```json
{
  "success": true,
  "sessionKey": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "data": { "userId": "0000000000P7", "fullName": "..." }
}
```

`sessionKey` → token taleplerinde kullanılır, saklanır.
`data` → API kullanıcısına aittir; **bu senaryoda kullanılmaz** (işlem yapılacak personel sicil no ile belirlenir).

### POST /ldaplogin

LDAP (Active Directory) kullanıcıları için. Parametreler ve yanıt `/securelogin` ile aynıdır. API kullanıcısı standart bir Praxapp kullanıcısı olduğundan bu uygulama `/securelogin` kullanır.

### POST /getToken

Her Praxapp Gateway çağrısından önce çağrılmalıdır. Token tek seferlik kullanımlıktır.

| Parametre | Açıklama |
|---|---|
| `key` | Login'den alınan `sessionKey` |
| `clientIP` | Login'deki `clientIP` ile **birebir aynı** değer |

**Yanıt (200, text/plain):** Token string'i.

| HTTP | Anlamı |
|---|---|
| 400 | Eksik parametre |
| 401 | Hatalı kimlik bilgisi veya geçersiz/expire olmuş sessionKey |
| 403 | IP uyuşmazlığı — login ve getToken'daki clientIP farklı |
| 429 | Rate limit aşıldı — 5 dakika içinde 5'ten fazla login |
| 503 | TokenProvider Praxapp Gateway'e ulaşamıyor |
| 504 | Praxapp Gateway zaman aşımı |

---

## Oturum Yönetimi

**Kritik kural: Her API çağrısında login olmayın.** Login Praxapp Gateway'e maliyetli bir istek atar ve rate limit'e tabidir.

### Oturum Süresi

| Durum | Davranış |
|---|---|
| `getToken` çağrıldı | Oturum 60 dakika uzar (sliding expiration) |
| 60 dakika boyunca hiç `getToken` çağrılmadı | Oturum expire olur |
| Login tarihinden itibaren 1 gün geçti | Kullanılsa da kullanılmasa da oturum expire olur |

### Rate Limit

Aynı `username + apiKey + clientIP` kombinasyonu için **5 dakika içinde en fazla 5 login** yapılabilir. Aşılırsa `429` döner.

### Önerilen Akış

```
Uygulama başlarken → securelogin → sessionKey'i sakla
Her API çağrısında → getToken → Gateway isteği
401 alınırsa       → securelogin → sessionKey'i güncelle → tekrar dene
```

> Tek API kullanıcısı kullanıldığı için `sessionKey` tüm personel işlemleri arasında paylaşılır. `sessionKey`'i kalıcı olarak saklayıp uygulama başlangıçlarında yeniden login yerine `getToken` ile devam etmek, rate limit'e takılmamak açısından önerilir.

---

## Praxapp Gateway İsteği

```
POST https://<gateway>/?appId=<ApiKey>&binding=json&op=<operasyon>
Authorization: Basic <base64(AuthUser:AuthPass)>
X-Forwarded-For: <clientIP — login/getToken ile aynı>
Content-Type: application/json

{ "token": "<getToken'dan alınan token>", ...operasyon parametreleri... }
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
| `Person` | Entity | Rezervasyonu yapan personel |

> Rezervasyonlar **personel sicil no'su** ile filtrelenir. `queryReservation` sorgusunda `Person.Key` alanı personelin sicil no'su ile eşleştirilir (bkz. queryReservation).

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
{ "token": "...", "query": "Alan >= @baslangic AND Diger = @sicil", "args": ["deger0", "deger1"] }
```

Parametre adları (`@baslangic`, `@sicil`, `@p0`…) anlamsızdır; Praxapp bunları query string içinde **soldan sağa görünüm sırasına** göre `args` dizisiyle eşleştirir. İlk parametre adı → `args[0]`, ikincisi → `args[1]`.

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

Belirtilen personelin rezervasyonlarını getirir. Personel **sicil no'su** `Person.Key` alanı üzerinden filtrelenir.

```json
{
  "token": "...",
  "query": "Date >= @p0 AND Person.Key = @p2",
  "args": ["2026-05-12T00:00:00.000Z", "12345"]
}
```

`args[0]` → tarih filtresi (`@p0`), `args[1]` → personel sicil no (`@p2`).

> **Uyarı:** `query` parametresi boş string (`""`) olarak gönderilirse Praxapp ilgili tablodaki tüm kayıtları döndürür. `queryReservation` gibi personel bazlı filtreleme gerektiren sorgularda `query` her zaman doldurulmalı; aksi halde başka personelin rezervasyonları da dönerek karışıklığa yol açar.

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

### CreateReservationOnBehalf

Belirtilen **sicil no'lu personel adına** yeni rezervasyon oluşturur. Tek API kullanıcısı ile çalışıldığı için klasik `createReservation` yerine bu operasyon kullanılır — rezervasyonun hangi personele ait olacağı `p1` (sicil no) ile belirlenir.

```json
{
  "token": "...",
  "p1": "<personel sicil no>",
  "p2": "<SeatOption.ID veya null>",
  "p3": "<TimeInterval.ID veya null>",
  "p4": "2026-05-15T00:00:00Z"
}
```

| Parametre | Zorunlu | Açıklama |
|---|---|---|
| `p1` | Evet | İşlem yapılacak personelin sicil no'su |
| `p2` | Hayır | `SeatOption.ID` — null → "Yemek istemiyorum" |
| `p3` | Hayır | `TimeInterval.ID` — null → "Yemek istemiyorum" |
| `p4` | Evet | Rezervasyon tarihi — UTC gece yarısı, `yyyy-MM-ddT00:00:00Z` |

**Başarılı yanıt:** `{ "HasErrors": false, "Messages": [] }`

**Hata yanıtı:**
```json
{ "HasErrors": true, "Messages": [{ "IsError": true, "Content": "Mükerrer kayıt..." }] }
```

Aynı personel + aynı gün için ikinci rezervasyon oluşturulmaya çalışılırsa `HasErrors: true` döner; bu durumda `updateReservation` kullanılmalıdır.

### updateReservation

Mevcut rezervasyonun menüsünü veya zaman dilimini değiştirir. `e` içine yalnızca değiştirilecek alanlar gönderilir. Rezervasyon `EID` ile belirlendiği için bu operasyonda sicil no gerekmez.

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
Başarılı ve hata yanıtı `CreateReservationOnBehalf` ile aynı yapıdadır.

---

## Rezervasyon Akışı

```
0. Sicil no girişi    → İşlem yapılacak personel belirlenir (UI)
1. securelogin        → sessionKey (API kullanıcısı ile)
2. queryOptionTypeModel → Menü tipleri (statik, bir kez alınır)
3. queryTimeInterval  → Yemekhane saat dilimleri ve kapasiteler (statik)
4. querySeatOption    → Bu haftanın günlük menüleri
5. queryReservation   → Personelin (sicil no) mevcut rezervasyonları

─── Demo uygulama döngüsü ───────────────────────────────────────────
6. Tarih seçimi       → SeatOption'lar ve mevcut rezervasyon durumu gösterilir
7. Menü seçimi        → 0 = Yemek istemiyorum
8. GetCapacityInfo    → Seçilen yemekhane + tarih için anlık doluluk
9. TimeInterval seçimi → Müsait = Kapasite - Doluluk
10. CreateReservationOnBehalf → Seçilen tarihte rezervasyon yoksa (sicil no ile)
    updateReservation         → Seçilen tarihte rezervasyon varsa (EID otomatik belirlenir)
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
| `CreateReservationOnBehalf p4` | `yyyy-MM-ddT00:00:00Z` |
| API yanıtlarında gelen `Date` | `yyyy-MM-ddT03:00:00+03:00` |

`CreateReservationOnBehalf`'ta `T00:00:00+03:00` gönderilirse sunucuda bir önceki güne kaydedilir; `T00:00:00Z` kullanılmalıdır.

### clientIP belirleme

TokenProvider iki ayrı IP doğrulaması yapar: form body'deki `clientIP` (sizin gönderdiğiniz) ve TCP bağlantısından otomatik tespit edilen `SenderIP`. Her ikisi de login ve getToken çağrıları arasında birebir aynı olmalıdır.

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

| Ortam | TokenProvider URL | Gateway URL |
|---|---|---|
| QA | `https://praxappqa.aksa.com.tr/tokenProvider/api/token` | `https://praxappqa.aksa.com.tr/gateway` |
| Production | `https://praxapp.aksa.com.tr/tokenProvider/api/token` | `https://praxapp.aksa.com.tr/gateway` |

`appsettings.json → TokenProviderUrl` / `GatewayUrl` ve `TokenProviderClient.GetLocalIp()` içindeki host adresi production geçişinde güncellenmelidir.

---

## Kurulum ve Çalıştırma

1. **Yapılandırma dosyasını oluşturun** — örnek dosyayı kopyalayıp gerçek değerlerle doldurun:

   ```
   copy appsettings.example.json appsettings.json
   ```

   `appsettings.json` içindeki `TokenProviderUrl`, `GatewayUrl`, `ApiKey`, `AuthUser`/`AuthPass` ve `ApiUser`/`ApiPassword` değerlerini ortamınıza göre girin (bkz. [Yapılandırma](#yapılandırma-appsettingsjson)).

2. **Derleyin:**

   ```
   dotnet build
   ```

3. **Çalıştırın:**

   ```
   dotnet run
   ```

   Uygulama açıldığında önce işlem yapılacak personelin **sicil no'sunu** ister, ardından API kullanıcısı ile login olup rezervasyon akışını başlatır.
