# FlowSharp

> 🌐 **Languages:** [English](README.md) · [Türkçe](README.tr.md)

**C# / .NET 10 ve Blazor** ile geliştirilmiş, kurumsal seviye **workflow otomasyon** platformu. Düğüm tabanlı (node-based) görsel akış tasarımcısı, gerçek çalışan node'lar, AI ajan desteği, zamanlanmış/webhook tetikleyiciler ve **çalışırken yüklenebilen topluluk plugin sistemi** içerir.

---

## İçindekiler

- [Özellikler](#özellikler)
- [Mimari](#mimari)
- [Kullanılan Kütüphaneler](#kullanılan-kütüphaneler)
- [Hazır Yüklü Node'lar](#hazır-yüklü-nodelar)
- [Gereksinimler](#gereksinimler)
- [Nasıl Kurulur](#nasıl-kurulur)
- [Yapılandırma (appsettings.json)](#yapılandırma-appsettingsjson)
- [Roller ve Yetkiler](#roller-ve-yetkiler)
- [Plugin Sistemi (Topluluk Node'ları)](#plugin-sistemi-topluluk-nodeları)
- [Yeni Node Yazmak](#yeni-node-yazmak)
- [Webhook ve Yanıt (Respond to Webhook)](#webhook-ve-yanıt-respond-to-webhook)
- [Proje Yapısı](#proje-yapısı)
- [Sponsorluk ve Destek](#sponsorluk-ve-destek)
- [Lisans](#lisans)
- [Sorun Giderme](#sorun-giderme)
- [Katkıda Bulunma](#katkıda-bulunma)

---

## Özellikler

- 🎨 **Görsel Workflow Tasarımcısı** — node ekleme, bağlama, kategori/arama filtresi, parametre düzenleme.
- ⚡ **Gerçek Çalışan Node'lar** — HTTP, e-posta (SMTP/IMAP), PostgreSQL, Slack/Telegram/Discord, mantık (IF/Switch/Filter/Merge), veri dönüşümü ve JavaScript (Jint) kodu.
- 🤖 **AI Agent** — Semantic Kernel üzerinden OpenAI, Azure OpenAI, Anthropic, Gemini, Groq, Mistral, Cohere, HuggingFace, OpenRouter ve Ollama; araç (tool) bağlama desteği.
- ⏰ **Tetikleyiciler** — Manuel, Cron zamanlı, Webhook (senkron yanıt dönebilen), IMAP e-posta ve Chat.
- 🔌 **Canlı Plugin Sistemi** — `plugins/` klasörüne bırakılan ham `.cs` dosyaları **Roslyn** ile çalışırken derlenir; ana uygulama yeniden derlenmeden node eklenir.
- 🛒 **Admin Marketplace** — GitHub deposundan plugin indir, kur, yeniden yükle, kaldır.
- 🔐 **Kimlik & Yetki** — ASP.NET Core Identity + rol/permission policy altyapısı.
- 📡 **Gerçek Zamanlı** — SignalR ile canlı node çalışma durumu (Redis veya in-memory fallback).
- 🧮 **Expression Motoru** — `{{ $json.alan }}`, `{{ $node["Ad"].json }}`, `{{ $now }}` vb.; tasarımcıda **canlı doğrulama** (yeşil/kırmızı).
- 📝 **Loglama** — Serilog ile konsol ve günlük dosyaları.

---

## Mimari

| Katman | Teknoloji |
|---|---|
| UI | Blazor Web App (interactive server render) |
| Backend | ASP.NET Core (.NET 10) |
| Gerçek zamanlı | SignalR + Redis (ya da in-memory) |
| Veritabanı | PostgreSQL |
| ORM | EF Core + JSONB workflow tanımı |
| Kimlik | ASP.NET Core Identity |
| Yetki | rol/permission policy (`AppPermissions`) |
| Kuyruk | PostgreSQL tabanlı `workflow_jobs` tablosu |
| Worker | ayrı `BackgroundService` projesi |
| Plugin | Roslyn runtime derleme + `AssemblyLoadContext` |
| AI | Microsoft Semantic Kernel |
| Loglama | Serilog (console + file) |

---

## Kullanılan Kütüphaneler

Aşağıdaki NuGet paketleri projede kullanılır:

| Paket | Sürüm | Kullanım |
|---|---|---|
| `Microsoft.SemanticKernel` | 1.77.0 | AI Agent ve sohbet modelleri |
| `MailKit` | 4.17.0 | SMTP gönderme ve IMAP okuma |
| `Jint` | 4.9.2 | Code node — sandbox'lı JavaScript |
| `CsvHelper` | 33.0.1 | CSV node — CSV oku/yaz |
| `AngleSharp` | 1.1.2 | HTML Extract node — CSS selector ile ayrıştırma |
| `ClosedXML` | 0.104.2 | Spreadsheet node — Excel (.xlsx) okuma |
| `Microsoft.Data.Sqlite` | 10.0.0 | RAG — SQLite vektör deposu (workspace başına) |
| `SmartComponents.LocalEmbeddings` | 0.1.0-preview10148 | RAG — yerel/in-process embedding (gömülü ONNX) |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.2 | PostgreSQL + EF Core |
| `Microsoft.EntityFrameworkCore.Design` / `.Tools` | 10.0.8 | Migration araçları |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.8 | Kimlik depolama |
| `Microsoft.CodeAnalysis.CSharp` (+ Workspaces) | 5.3.0 | **Roslyn** — plugin runtime derleme |
| `StackExchange.Redis` | 2.13.17 | SignalR olay yayını (çok-process) |
| `Cronos` | 0.13.0 | Cron ifadesi çözümleme (zamanlayıcı) |
| `Serilog.AspNetCore` / `Sinks.Console` / `Sinks.File` | 10.0.0 / 6.1.1 / 7.0.0 | Loglama |
| `Microsoft.Extensions.Hosting` | 10.0.8 | Worker host altyapısı |

---

## Hazır Yüklü Node'lar

Tüm node'lar `INodeType` implementasyonu olarak **otomatik keşfedilir** ve palette'te kategorilerine göre görünür.

### Tetikleyiciler (Trigger)
| Anahtar | Ad | Açıklama |
|---|---|---|
| `manual.trigger` | Manual Trigger | Akışı elle başlatır |
| `schedule.trigger` | Schedule Trigger | Cron ifadesine göre periyodik çalışır |
| `webhook.trigger` | Webhook | Gelen HTTP isteğiyle başlar (senkron yanıt verebilir) |
| `email.imap.trigger` | Email Trigger (IMAP) | Gelen kutusuna yeni e-posta düşünce tetikler |
| `chat.trigger` | AI Chat UI | Sohbet arayüzünden tetikler |
| `flow.executeWorkflowTrigger` | Execute Workflow Trigger | Başka bir workflow tarafından çağrılınca başlar |
| `error.trigger` | Error Trigger | Bir workflow hata verince çalışır |

### Çekirdek / Mantık (Core)
| Anahtar | Ad | Açıklama |
|---|---|---|
| `if.condition` | IF | Koşula göre true/false dallandırma |
| `switch.condition` | Switch | Çoklu kola dallandırma |
| `filter.items` | Filter | Koşula uyan item'ları geçirir |
| `merge.items` | Merge | Birden çok girişi birleştirir |
| `set.fields` | Set | Alan ekler/değiştirir |
| `no.op` | No Operation | Veriyi olduğu gibi geçirir |
| `code.javascript` | Code | Sandbox'lı JavaScript (Jint) çalıştırır |
| `flow.wait` | Wait | Akışı belirtilen süre kadar bekletir |
| `flow.stopAndError` | Stop And Error | Akışı özel hata mesajıyla durdurur |
| `flow.executeWorkflow` | Execute Workflow | Bir alt-workflow'u çalıştırır ve çıktısını döner |
| `flow.loopOverItems` | Loop Over Items | Item'ları parti parti (batch) işler; `loop`/`done` çıkışları |

### Veri / Dönüşüm (Data)
| Anahtar | Ad | Açıklama |
|---|---|---|
| `sort.items` | Sort | Bir alana göre sıralar |
| `limit.items` | Limit | Maksimum item sayısını sınırlar |
| `aggregate.items` | Aggregate | Tüm item'ları tek item'da toplar |
| `split.out` | Split Out | Bir dizi alanını ayrı item'lara böler |
| `datetime.action` | Date & Time | Tarih/saat üretir veya biçimlendirir |
| `transform.crypto` | Crypto | Hash / HMAC / Base64 işlemleri |
| `transform.csv` | CSV | Item'ları CSV'ye çevirir veya CSV'yi item'lara ayrıştırır |
| `transform.htmlExtract` | HTML Extract | HTML'den CSS selector ile veri çıkarır |
| `transform.spreadsheet` | Spreadsheet | Yüklenen Excel/CSV dosyasını okur; her satır bir item olur |

### HTTP
| Anahtar | Ad | Açıklama |
|---|---|---|
| `http.request` | HTTP Request | Metot seçilebilir tam REST isteği |
| `http.get` / `http.post` / `http.put` / `http.patch` / `http.delete` | HTTP GET/POST/... | Sabit metotlu istekler |
| `webhook.response` | Respond to Webhook | Webhook çağıranına özel yanıt döner |

### Veritabanı (Database)
| Anahtar | Ad | Açıklama |
|---|---|---|
| `postgres.query` | Postgres | SQL sorgusu çalıştırır (select/execute) |

### İletişim (Communication)
| Anahtar | Ad | Açıklama |
|---|---|---|
| `email.send` | Send Email | SMTP ile e-posta gönderir (MailKit) |
| `telegram.message` | Telegram | Telegram'a mesaj gönderir |
| `slack.message` | Slack | Slack kanalına mesaj gönderir |
| `discord.message` | Discord | Discord webhook'una mesaj gönderir |

### AI
| Anahtar | Ad | Açıklama |
|---|---|---|
| `ai.agent` | AI Agent | Araç çağırabilen AI ajanı |
| `openai.chat` / `azureopenai.chat` / `anthropic.chat` / `gemini.chat` / `groq.chat` / `mistral.chat` / `cohere.chat` / `huggingface.chat` / `openrouter.chat` / `ollama.chat` | *Provider* Chat | Doğrudan sohbet tamamlama |
| `*.chatmodel` | *Provider* Chat Model | AI Agent'a bağlanan model alt-node'u |
| `tool.httpRequest` | HTTP Request Tool | Ajana HTTP aracı |
| `tool.calculator` | Calculator | Ajana hesap makinesi aracı |
| `rag.insert` | Vector Store: Insert | Metinleri embedding'leyip SQLite vektör deposuna ekler (RAG) |
| `rag.query` | Vector Store: Query | Vektör deposunda anlamsal arama yapar (RAG) |

---

## Gereksinimler

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **PostgreSQL** (varsayılan: `localhost:5432`)
- **Docker** (Redis ve/veya Postgres'i container ile çalıştırmak için — opsiyonel)
- Redis (opsiyonel; yoksa in-memory fallback devreye girer)

---

## Nasıl Kurulur

### ⚡ Hızlı Kurulum (Docker Compose)

Tüm FlowSharp stack'ini (PostgreSQL, Redis, Web UI ve Worker) tek bir komutla ayağa kaldırmak için en kolay yol Docker Compose kullanmaktır:

```bash
docker compose up -d --build
```

Konteynerler çalışmaya başladıktan sonra tarayıcınızdan `http://localhost:8080` adresine gidebilirsiniz.
* **Varsayılan Admin Bilgileri:** `admin@flowsharp.local` / `Admin!2345`

---

### 💻 Geliştirici Kurulumu (Lokal Çalıştırma)

Bileşenleri geliştirme amaçlı olarak yerel makinenizde ayrı ayrı çalıştırmak isterseniz:

```powershell
# 1) Depoyu klonla
git clone https://github.com/FlowSharp/FlowSharp.git
cd FlowSharp

# 2) Yardımcı servisleri başlat (Redis/Postgres)
docker compose up -d

# 3) Paketleri geri yükle ve derle
dotnet restore
dotnet build

# 4) Veritabanı şemasını uygula (migration)
dotnet ef database update `
  --project src/FlowSharp.Infrastructure `
  --startup-project src/FlowSharp.Web

# 5) Web ve Worker'ı ayrı terminallerde çalıştır
dotnet run --project src/FlowSharp.Web
dotnet run --project src/FlowSharp.Worker
```

> 💡 **Tek process modu:** `appsettings.json` içinde `"Worker": { "RunInWebProcess": true }` yaparsanız Worker, web uygulamasının içinde çalışır; ayrı terminal/Worker gerekmez.

> 💡 **Otomatik migration:** `"Database": { "ApplyMigrationsOnStartup": true }` ile açılışta migration otomatik uygulanır.

Uygulama `https://localhost:7163` ve `http://localhost:5094` üzerinde dinler.

---

## Yapılandırma (appsettings.json)

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=flowsharp_db;Username=postgres;Password=Postgres"
  },
  "Database": { "ApplyMigrationsOnStartup": false },
  "Redis": { "ConnectionString": "localhost:6379" },
  "Worker": { "RunInWebProcess": false },

  // Çalışma kayıtları
  "Executions": { "SaveData": "All", "MaxCount": 1000, "MaxAgeDays": 30 },

  // Plugin sistemi
  "Plugins": {
    "Path": "plugins",
    "OfficialMarketplaceUrl": "https://github.com/FlowSharp/FlowSharp.plugins"
  },

  // Üretimde MUTLAKA ayarlayın — credential şifreleme anahtarı
  "Security": { "CredentialEncryptionKey": "<güçlü-rastgele-anahtar>" },

  // İlk admin kullanıcısı (yalnız hiç kullanıcı yoksa oluşturulur)
  "Seed": {
    "Enabled": true,
    "Admin": { "Email": "admin@flowsharp.local", "Password": "Admin!2345" }
  }
}
```

---

## Roller ve Yetkiler

Açılışta üç rol seed edilir: **Admin**, **Editor**, **Viewer**.

| Permission | Açıklama | Admin | Editor | Viewer |
|---|---|:--:|:--:|:--:|
| `workflows.read` | Workflow görüntüleme | ✅ | ✅ | ✅ |
| `workflows.write` | Workflow düzenleme | ✅ | ✅ | — |
| `workflows.execute` | Workflow çalıştırma | ✅ | ✅ | — |
| `executions.read` | Çalışma kayıtları | ✅ | ✅ | ✅ |
| `plugins.manage` | Marketplace / plugin yönetimi | ✅ | — | — |

---

## Plugin Sistemi (Topluluk Node'ları)

Topluluk, ana uygulamayı yeniden derlemeden **Roslyn destekli dinamik yükleme** sistemimiz ile yeni node ekleyebilir.

### Nasıl Çalışır
1. `plugins/` altındaki **her klasör** bir plugin'dir.
2. Klasördeki tüm `.cs` dosyaları (alt klasörler dahil) açılışta **Roslyn** ile derlenir.
3. Üretilen assembly collectible bir `AssemblyLoadContext`'e yüklenir; içindeki `INodeType` ahadları node kaydına eklenir.

### Marketplace'ten Kurulum (Admin)
1. Sol menüden **Marketplace**'i açın (`plugins.manage` yetkisi gerekir).
2. GitHub URL'sini girin veya "Resmi adresi kullan" seçeneğine tıklayın.
3. **Kur** butonuna basın. Eklenti indirilir, derlenir ve anında canlı olarak yüklenir.

---

## Yeni Node Yazmak

Örnek bir plugin düğümü: `plugins/Sample/HelloNode.cs`:

```csharp
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace Community.Sample;

public sealed class HelloNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "community.hello",
        DisplayName: "Hello (Plugin)",
        Category: NodeCategory.Core,
        Kind: NodeKind.Action,
        Description: "Item'a selam ekler.",
        Parameters: [ new NodeParameterDefinition("name", "Name", NodeParameterType.String, DefaultValue: "Dunya") ],
        Tags: ["community"], Icon: "sparkles", Color: "#9b51e0");

    protected override Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var name = context.GetString("name", index) ?? "Dunya";
        var output = (JsonObject)item.Json.DeepClone();
        output["greeting"] = $"Merhaba, {name}!";
        return Task.FromResult<NodeItem?>(NodeItem.From(output));
    }
}
```

---

## Webhook ve Yanıt (Respond to Webhook)

- `webhook.trigger` ile başlayan bir workflow, gelen HTTP isteğinde **senkron** çalışır.
- Endpoint: `/webhook/{path}` — `path` ve `method` webhook node parametrelerinden eşlenir.

---

## Proje Yapısı

```
src/
├─ FlowSharp.Web            # Blazor UI, Identity, webhook endpoint, marketplace, plugins/
├─ FlowSharp.Worker         # Kuyruktan job işleyen BackgroundService
├─ FlowSharp.Domain         # Workflow, execution, queue, node, security modelleri
├─ FlowSharp.Application    # Sözleşmeler: INodeType, INodeRegistry, IPluginManager, expression
├─ FlowSharp.Infrastructure # EF Core, kuyruk, runner, motor, plugin manager (Roslyn), scheduler
└─ FlowSharp.Nodes          # Tüm hazır node'lar (HTTP, AI, DB, iletişim, core, transform)
```

---

## Sponsorluk ve Destek

Şirketiniz iş kritik otomasyon süreçleri için FlowSharp kullanıyorsa, projeyi desteklemenin yolları:

*   **Kurumsal Danışmanlık:** Özel kurumsal eklenti (node) geliştirme, yüksek kullanılabilirlikli kurulumlar veya profesyonel entegrasyon yardımı mı gerekiyor? Profesyonel destek anlaşmaları için bizimle iletişime geçebilirsiniz.
*   **GitHub Sponsors:** FlowSharp geliştirmesini doğrudan GitHub üzerinden destekleyerek yol haritamıza katkıda bulunabilir ve sponsor ayrıcalıklarından faydalanabilirsiniz.
*   **Premium Eklentiler:** Büyük ölçekli ve kurumsal süreçler için özel olarak tasarlanmış premium işlevlere sahip eklentilere erişebilirsiniz.

---

## Lisans

Bu proje **Elastic License 2.0 (ELv2)** ile lisanslanmıştır.

*   **Kullanım Serbestliği:** FlowSharp'ı bireysel projelerinizde veya kendi organizasyonunuz/şirketiniz içinde ücretsiz olarak çalıştırabilirsiniz.
*   **SaaS Satış Yasağı:** Projeyi üçüncü şahıslara ücretli/ücretsiz bir bulut/SaaS servisi olarak kiralayamaz veya satamazsınız.
*   **Telif Hakları ve Marka:** Projedeki tüm "FlowSharp" markalamasını, lisans başlıklarını ve telif bildirimlerini aynen korumanız zorunludur.

Detaylar için [LICENSE.md](LICENSE.md) dosyasına göz atabilirsiniz.

---

## Katkıda Bulunma

Topluluk katkılarını memnuniyetle karşılıyoruz! Kod kalitesini korumak ve mükerrer çalışmaları önlemek için **"Önce Konuş, Sonra Kodla" (Issue Bağımlılığı)** modelini uyguluyoruz.

Core (çekirdek) repoda değişiklik yapmadan önce lütfen [Katkı Sağlama Kılavuzumuzu](CONTRIBUTING.tr.md) okuyun.

