# FoodApp — Geliştirici Rehberi

## Proje Amacı

AKSA çalışanlarının yemek rezervasyonlarını yönetmesini sağlayan bir .NET 8 konsol uygulaması. Praxapp Gateway üzerindeki rezervasyon operasyonlarını kullanır; kimlik doğrulama için PraxappTokenProvider'a bağımlıdır.

---

## Mimari

```
Kullanıcı ──► Program.cs (UI)
                 ├─ TokenProviderClient  ──POST──► PraxappTokenProvider (ashx)
                 │                                      └──► Praxapp Gateway (logon)
                 └─ PraxappClient        ──POST──► Praxapp Gateway (rezervasyon ops)
```

**Dosyalar:**

| Dosya | Amaç |
|---|---|
| `Program.cs` | Konsol UI, ana akış |
| `TokenProviderClient.cs` | TokenProvider ile iletişim (login / getToken) |
| `PraxappClient.cs` | Gateway operasyon çağrıları |
| `AppState.cs` | Uygulama durumu (SeatOptions, Reservations) |
| `Config.cs` | Yapılandırma sabitleri |

---

## Kimlik Doğrulama Akışı

### 1. Login (TokenProvider)

```
POST TokenService.ashx
  action=login
  username=...
  password=...
  clientIP=...   ← sonraki getToken çağrılarında tutarlı olmalı
  apiKey=...

Yanıt: { "success": true, "sessionKey": "..." }
```

### 2. Token Al (her Gateway isteği öncesi)

```
POST TokenService.ashx
  action=getToken
  key={sessionKey}
  clientIP=...   ← login'deki clientIP ile aynı olmalı

Yanıt: düz metin token
```

> **clientIP ve User-Agent:** TokenProvider bu değerleri oturum güvenliği için saklar. FoodApp kendi IP/User-Agent değerlerini gönderebilir — son kullanıcının gerçek bilgileri olması gerekmiyor. Önemli olan: login ve getToken çağrılarında **tutarlı** olması.

### 3. Gateway İsteği

```
POST {GatewayUrl}/?appId={apiKey}&binding=json&op={operasyon}
Header: Authorization: {token}
Body: { "token": "{token}", ...parametreler... }
```

---

## Praxapp Gateway — Rezervasyon Operasyonları

### Kullanılan Operasyonlar

| Operasyon | Amaç |
|---|---|
| `querySeatOption` | Mevcut menü seçeneklerini listele |
| `queryReservation` | Mevcut rezervasyonları listele |
| `GetCapacityInfo` | Seçilen menü için zaman aralıklarını getir |
| `createReservation` | Yeni rezervasyon oluştur |
| `updateReservation` | Mevcut rezervasyonu güncelle |

### Query Operasyonları

```json
{
  "token": "...",
  "query": "Tarih >= @p0 AND Tarih <= @p1",
  "args": ["2026-05-01T00:00:00Z", "2026-05-31T00:00:00Z"]
}
```

> **Parametre isimleri (`@p0`, `@p1` vb.) Praxapp tarafından kullanılmaz.** Eşleştirme pozisyonel yapılır: `args[0]` sorgu metnindeki ilk parametreye karşılık gelir, isim farketmez.

> **Uyarı:** `query` boş string (`""`) olarak gönderilirse Praxapp tablodaki tüm kayıtları döner. Her zaman filtrelenmiş sorgu gönder.

### createReservation

```json
{
  "token": "...",
  "e": {
    "Date":         "2026-05-12T00:00:00Z",
    "SeatOption":   "option-id veya null",
    "TimeInterval": "interval-id veya null"
  }
}
```

> **Tarih formatı:** `yyyy-MM-ddT00:00:00Z` (UTC midnight) kullanılmalı. `T00:00:00+03:00` gönderilirse Praxapp'ta bir önceki güne kaydedilir.

### updateReservation

```json
{
  "token": "...",
  "EID": "rezervasyon-id",
  "e": {
    "SeatOption":   "option-id veya null",
    "TimeInterval": "interval-id veya null"
  }
}
```

> `e` içinde yalnızca değişen alanlar gönderilir. `Date` güncellenmez.

### Yemek İstemiyorum

Her iki operasyonda da `SeatOption: null` ve `TimeInterval: null` göndermek "yemek istemiyorum" seçeneğini temsil eder.

### Yanıt Formatı (tüm operasyonlar)

```json
{ "HasErrors": false, "Data": {...}, "Messages": [...] }
```

`HasErrors: true` ise `Messages` dizisinde hata detayı bulunur.

---

## UI Akışı

1. Kullanıcı adı ve şifre prompt'tan alınır (şifre: renk maskeleme ile, paste destekli)
2. Login → sessionKey alınır
3. `querySeatOption` ve `queryReservation` ile mevcut veriler çekilir
4. **Tarih listesi gösterilir** — her tarih altında menü seçenekleri, rezervasyon varsa yanında bilgisi
5. Kullanıcı tarih seçer → menü seçer (0: Yemek istemiyorum, G: geri)
6. Menü seçildiyse `GetCapacityInfo` ile zaman aralıkları gösterilir
7. Auto create/update: o tarih için rezervasyon varsa `updateReservation`, yoksa `createReservation`
8. İşlem sonrası `queryReservation` yenilenir, tarih listesine dönülür (Q: çıkış)

---

## Build

```
dotnet build
```

---

## Ortam URL'leri

| Ortam | TokenProvider | Gateway |
|---|---|---|
| QA | `http://tokenqa.aksa.com.tr/...` | `https://praxappqa.aksa.com.tr` |
| Production | `http://token.aksa.com.tr/...` | `https://praxapp.aksa.com.tr` |

---

## Kurallar

- Commit mesajlarına ve kaynak kod dosyalarına AI referansı ekleme (Co-Authored-By dahil).
