# FlowSharp — Kubernetes Dağıtımı

Bu klasör, FlowSharp'ı Kubernetes üzerinde **yatay ölçeklenebilir** çalıştırmak için manifest'leri içerir:
Web (Blazor Server, çoklu instance) + Worker (kuyruk tüketici, otomatik ölçeklenen) + PgBouncer + Redis + Postgres.

## Dosyalar

| Dosya | İçerik |
|------|--------|
| `00-namespace.yaml` | `flowsharp` namespace |
| `10-secret.example.yaml` | **Örnek** secret (gerçekte `kubectl create secret` kullanın) |
| `11-configmap.yaml` | Gizli olmayan ayarlar (provider, redis, rate limit, OTel, worker concurrency) |
| `20-redis.yaml` | Redis (çoklu-proses Pub/Sub) |
| `21-postgres.yaml` | Postgres StatefulSet (dev/test; üretimde yönetilen DB önerilir) |
| `22-pgbouncer.yaml` | PgBouncer connection pooling (transaction mode) |
| `40-web.yaml` | Web Deployment + Service + Ingress |
| `41-worker.yaml` | Worker Deployment |
| `50-web-hpa.yaml` | Web HPA (CPU) |
| `51-worker-hpa.yaml` | Worker HPA (CPU) — **basit** seçenek |
| `52-worker-keda-scaledobject.yaml` | Worker ölçekleme (**kuyruk derinliği**, KEDA) — **önerilen** |

## Önkoşullar

- Bir Kubernetes kümesi + `kubectl`
- **metrics-server** (HPA'nın CPU okuması için)
- Ingress controller (örn. ingress-nginx)
- Worker'ı kuyruk derinliğine göre ölçeklemek için **KEDA** (opsiyonel ama önerilen)
- İki imaj, erişilebilir bir registry'de:
  ```bash
  docker build --target web    -t your-registry/flowsharp-web:latest .
  docker build --target worker -t your-registry/flowsharp-worker:latest .
  docker push your-registry/flowsharp-web:latest
  docker push your-registry/flowsharp-worker:latest
  ```
  `40-web.yaml` ve `41-worker.yaml` içindeki `your-registry/...` değerlerini güncelleyin.

## Dağıtım

```bash
# 1) Namespace
kubectl apply -f 00-namespace.yaml

# 2) Secret (örnek dosya yerine üretin)
kubectl -n flowsharp create secret generic flowsharp-secrets \
  --from-literal=CredentialEncryptionKey="$(openssl rand -base64 32)" \
  --from-literal=PostgresPassword="GÜÇLÜ-BİR-PAROLA"

# 3) Config + veri katmanı
kubectl apply -f 11-configmap.yaml -f 20-redis.yaml -f 21-postgres.yaml -f 22-pgbouncer.yaml

# 4) Uygulama
kubectl apply -f 40-web.yaml -f 41-worker.yaml

# 5) Otomatik ölçekleme
kubectl apply -f 50-web-hpa.yaml
#   Worker için BİRİNİ seçin:
kubectl apply -f 52-worker-keda-scaledobject.yaml   # KEDA varsa (önerilen)
# kubectl apply -f 51-worker-hpa.yaml               # KEDA yoksa (CPU tabanlı)
```

> **Uyarı:** `51-worker-hpa.yaml` ve `52-worker-keda-scaledobject.yaml` aynı anda uygulanmamalı —
> ikisi de aynı Deployment'ı yönetir. KEDA kendi HPA'sını oluşturur.

## Ölçekleme mantığı

- **Web → CPU HPA:** Blazor Server bellek/CPU yoğun; CPU %70 hedefiyle 2–10 replica.
- **Worker → kuyruk derinliği (KEDA):** `workflow_jobs` tablosunda çalışmaya hazır (Pending +
  vakti gelmiş) iş sayısına göre ölçekler. `desiredReplicas = ceil(bekleyen / 20)`, 1–20 replica.
  AI/LLM gibi I/O-bound işlerde CPU düşük kalsa bile kuyruk birikince ölçeklenir — CPU HPA'nın
  zayıf kaldığı senaryo. KEDA yoksa CPU tabanlı `51-worker-hpa.yaml` makul bir alternatiftir.

## Notlar / üretim önerileri

- **Migration yarışı:** Şema migration'larını yalnız **web** uygular (`Database__ApplyMigrationsOnStartup=true`),
  worker'da kapalıdır. İlk dağıtımda birden çok web replica'sı aynı anda migrate etmeye çalışabilir;
  güvenli yol: ilk kurulumda web'i `replicas: 1` ile çıkarıp migration tamamlanınca ölçeklemek,
  veya ayrı bir migration Job çalıştırmak.
- **Postgres:** Buradaki StatefulSet dev/test içindir. Üretimde yönetilen Postgres kullanıp
  `22-pgbouncer.yaml` içindeki `DB_HOST`'u ona yönlendirin ve `21-postgres.yaml`'i atlayın.
- **PgBouncer transaction mode:** Sunucu-tarafı prepared statement desteklenmez; connection string'de
  `No Reset On Close=true;Max Auto Prepare=0` ile kapatıldı (bkz. `40/41`).
- **Blob offload:** `BlobStorage__Enabled` çoklu replica'da yalnız **paylaşılan (RWX)** bir volume ile
  açılmalı; aksi halde S3 sağlayıcısına geçin. Varsayılan kapalı.
- **OTel:** `OpenTelemetry__OtlpEndpoint` küme içi bir collector'a (örn. `otel-collector:4317`) işaret eder;
  collector yoksa `OpenTelemetry__Enabled: "false"` yapın.
