# FlowSharp Katkı Sağlama Kılavuzu

Öncelikle FlowSharp projesine katkıda bulunmak istediğiniz için çok teşekkür ederiz!

Projenin kararlılığını korumak, mimari bütünlüğünü bozmamak ve en önemlisi geliştiricilerin tasarım hedeflerimizle çelişen özellikler üzerinde boşuna vakit kaybetmesini engellemek için **"Önce Konuş, Sonra Kodla" (Issue Bağımlılığı)** kuralını uyguluyoruz.

---

## 🚫 Altın Kural: Önce Konuş, Sonra Kodla (Issue Bağımlılığı)

Eğer **Core (Çekirdek) kod tabanında** bir değişiklik yapmayı planlıyorsanız, kod yazmaya başlamadan veya Pull Request (PR) açmadan önce **MUTLAKA** bir Issue (Başlık) açıp bunu tartışmalısınız.

*   **Core (Çekirdek) Kod Tabanı nereleri kapsar?**
    *   `src/FlowSharp.Web`
    *   `src/FlowSharp.Worker`
    *   `src/FlowSharp.Domain`
    *   `src/FlowSharp.Application`
    *   `src/FlowSharp.Infrastructure`
*   **Neler Core kapsamına girmez?**
    *   `plugins/` klasörü altına yeni topluluk düğümleri (nodes) eklemek.
    *   Yazım hatalarını düzeltmek, net bir şekilde yeniden üretilebilen (reproduce) bug'ları fix'lemek veya dokümantasyonu güncellemek.

> [!IMPORTANT]
> **Önceden açılmış, onaylanmış ve size atanmış (assign edilmiş) bir Issue'ya bağlı olmayan tüm Core PR'ları incelenmeden doğrudan kapatılacaktır.**

### Süreç Nasıl İşler?
1.  **Issue Açın:** Ne değiştirmek istediğinizi, bunun neden gerekli olduğunu ve nasıl yapmayı planladığınızı anlatan bir Issue açın.
2.  **Onay Alın:** Proje yöneticisinin konsepti incelemesini ve onaylamasını bekleyin.
3.  **Size Atanmasını Bekleyin:** Onaylandıktan sonra yönetici Issue'yu size atayacaktır (assign). Bu sayede topluluk o dosya üzerinde sizin çalıştığınızı bilir ve çakışmalar (duplicate work) önlenir.
4.  **PR Gönderin:** Pull Request'inizi açtığınızda bunu ilgili Issue ile ilişkilendirin (örneğin `Closes #123`).

---

## 🔌 Yeni Düğüm (Node) ve Eklenti (Plugin) Katkıları

Yeni düğümler (workflow nodes) ekleyerek FlowSharp'ı zenginleştirmek istiyorsanız, bunları ana depoya değil, doğrudan topluluk eklenti depomuz olan **[FlowSharp Plugins](https://github.com/FlowSharp/plugins)** deposuna göndermelisiniz!

*   Eklentiler bağımsız çalıştığı için çekirdek mimariyi etkilemez ve **önceden onay almanız gerekmez.**
*   Geliştirdiğiniz eklentiyi test ettikten sonra doğrudan [FlowSharp Plugins](https://github.com/FlowSharp/plugins) deposuna Pull Request (PR) gönderebilirsiniz.
*   Düğümlerinizin `INodeType` yapısına uygun olduğundan ve gerekli hata yönetimini (exception handling) barındırdığından emin olun.

## 📝 Kod Düzeni ve Kılavuzlar

*   C# 12 / .NET 10 özelliklerini kullanın.
*   Kodunuzu varsayılan editör yapılandırmasına göre formatlayın.
*   Mümkünse birim testleri (unit tests) ekleyin.
