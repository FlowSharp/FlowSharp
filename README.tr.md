# FlowSharp

![FlowSharp workflow automation hero](docs/assets/flowsharp-hero.png)

FlowSharp, **C#**, **.NET 10** ve **Blazor** ile geliştirilen node tabanlı bir workflow otomasyon platformudur. Görsel workflow tasarımcısı, çalıştırılabilir otomasyon node'ları, AI ajan desteği, webhook ve zamanlanmış tetikleyiciler, arka plan worker'ları ve çalışma zamanında yüklenebilen C# plugin sistemi içerir.

![FlowSharp workflow designer](docs/assets/flowsharp-designer-mockup.png)

## Öne Çıkanlar

- Node paleti, bağlantılar, parametreler ve çalışma durumları olan görsel workflow tasarımcısı.
- HTTP, e-posta, PostgreSQL, mantık, veri dönüşümü, JavaScript, iletişim servisleri ve AI için çalıştırılabilir node'lar.
- Semantic Kernel tabanlı, model ve araç alt-node'ları destekleyen AI ajanları.
- Webhook, manuel, zamanlanmış, chat, IMAP, workflow ve hata tetikleyicileri.
- Runtime plugin sistemi: C# kaynak dosyalarını `plugins/` klasörüne bırakıp uygulamayı yeniden derlemeden yeni node yükleme.
- ASP.NET Core Identity, rol/permission policy altyapısı, şifreli credential saklama, SignalR canlı olaylar ve Serilog logları.

## Kullanılan Kütüphaneler

| Paket | Sürüm | Kullanım |
|---|---|---|
| `Microsoft.SemanticKernel` | 1.77.0 | AI Agent ve sohbet modelleri |
| `MudBlazor` | 9.5.0 | UI bileşenleri |
| `MailKit` | 4.17.0 | SMTP gönderimi ve IMAP okuma |
| `Jint` | 4.9.2 | Code node - sandbox JavaScript |
| `CsvHelper` | 33.0.1 | CSV node - CSV okuma/yazma |
| `AngleSharp` | 1.1.2 | HTML Extract node - CSS selector ayrıştırma |
| `ClosedXML` | 0.104.2 | Spreadsheet node - Excel (.xlsx) okuma |
| `Microsoft.Data.Sqlite` | 10.0.0 | RAG - SQLite vektör deposu |
| `SmartComponents.LocalEmbeddings` | 0.1.0-preview10148 | RAG - yerel/in-process embedding |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.2 | PostgreSQL + EF Core |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.8 | SQL Server + EF Core |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.8 | SQLite + EF Core |
| `Microsoft.EntityFrameworkCore.Design` / `.Tools` | 10.0.8 | Migration araçları |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.8 | Identity depolama |
| `Microsoft.CodeAnalysis.CSharp` / Workspaces | 5.3.0 | Roslyn runtime plugin derleme |
| `StackExchange.Redis` | 2.13.17 | Workflow olay backplane'i |
| `Cronos` | 0.13.0 | Cron ifadesi ayrıştırma |
| `Serilog.AspNetCore` / Sinks | 10.0.0 / 6.1.1 / 7.0.0 | Loglama |

## Hızlı Başlangıç

Docker Compose ile stack'i çalıştırın:

```bash
docker compose up -d --build
```

Tarayıcıdan açın:

```text
http://localhost:8080
```

Varsayılan admin hesabı:

```text
admin@flowsharp.local
Admin!2345
```

Varsayılan Docker Compose kurulumu uygulama veritabanı için SQLite, process'ler arası workflow olayları için Redis kullanır.

## Lokal Geliştirme

Gereksinimler:

- .NET 10 SDK
- Docker, Redis ve veritabanı servisleri için opsiyonel ama kullanışlıdır

Derleme:

```powershell
dotnet restore
dotnet build
```

Web uygulamasını çalıştırma:

```powershell
dotnet run --project src/FlowSharp.Web
```

Worker'ı ayrı terminalde çalıştırma:

```powershell
dotnet run --project src/FlowSharp.Worker
```

Tek process geliştirme modu için:

```json
{
  "Worker": {
    "RunInWebProcess": true
  }
}
```

## Veritabanı Desteği

FlowSharp şu EF Core provider'larını destekler:

- `Sqlite`
- `Postgres`
- `SqlServer`

Varsayılan lokal SQLite connection string:

```json
{
  "Database": {
    "Provider": "Sqlite",
    "ApplyMigrationsOnStartup": true
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=App_Data/flowsharp.db"
  }
}
```

PostgreSQL, SQL Server, Redis, plugin, credential ve production ayarları için [Configuration](docs/guide/configuration.md) dokümanına bakın.

## Dokümantasyon

- [Getting Started](docs/guide/getting-started.md)
- [Architecture](docs/guide/architecture.md)
- [Configuration](docs/guide/configuration.md)
- [Roles And Permissions](docs/guide/roles-and-permissions.md)
- [Built-in Nodes](docs/guide/built-in-nodes.md)
- [AI Agents](docs/guide/ai-agents.md)
- [Webhooks](docs/guide/webhooks.md)
- [Plugin Development](docs/guide/plugin-development.md)
- [Marketplace](docs/guide/marketplace.md)

## Proje Yapısı

```text
src/
|-- FlowSharp.Web            Blazor UI, Identity, designer, webhooks, marketplace
|-- FlowSharp.Worker         Kuyruktaki ve zamanlanmış işleri çalışan arka plan worker'ı
|-- FlowSharp.Domain         Workflow, execution, queue, credential ve node modelleri
|-- FlowSharp.Application    Interface'ler ve application contract'ları
|-- FlowSharp.Infrastructure EF Core, workflow engine, queue, plugins, scheduler
|-- FlowSharp.Nodes          Yerleşik workflow node'ları
```

## Lisans

FlowSharp **Elastic License 2.0 (ELv2)** ile lisanslanmıştır. Detaylar için [LICENSE.md](LICENSE.md) dosyasına bakın.

## Katkı

Pull request açmadan önce lütfen [CONTRIBUTING.tr.md](CONTRIBUTING.tr.md) dosyasını okuyun.
