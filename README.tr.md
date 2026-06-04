# Lian Li LCD Theme Editor

**Diller:** [English](README.en.md) | [Türkçe](README.tr.md) | [Русский](README.ru.md) | [简体中文](README.zh.md)

Lian Li L-Connect 3 LCD şablonları için resmi olmayan bir Windows tema editörü.

Bu editör Hydroshift II LCD cihaz ailesi için geliştirildi. L-Connect içinde her öğeyi tek tek elle düzenlemek zorunda kalmadan LCD şablon katmanlarını inceleyebilir, düzenleyebilir, ekleyebilir, sıralayabilir, önizleyebilir ve uygulayabilirsiniz.

> Bu proje Lian Li ile bağlantılı değildir ve resmi bir uygulama değildir. Lian Li'nin resmi Discord'da projeden bahsetmeme izin vermesine minnettarım, fakat editör sizde düzgün çalışmazsa lütfen bunu Lian Li desteğine taşımayın. Uygulama yerel L-Connect 3 şablon ve profil dosyalarını değiştirir; sevdiğiniz temaları düzenlemeden önce yedek alın.

## Ekran Görüntüsü

<img width="2546" height="1370" alt="image" src="https://github.com/user-attachments/assets/3fd774bc-e45a-44eb-822d-a93642ade68a" />

## Özellikler

- Var olan L-Connect 3 LCD şablon katmanlarını düzenleme.
- Kare ekranlı Hydroshift II LCD-S ve yuvarlak ekranlı Hydroshift II LCD-C desteği.
- Aktif şablon başka bir cihaz ailesine aitse otomatik yedek arama.
- LCD-S için kare, LCD-C için yuvarlak maske ile önizleme.
- Metin, veri, görsel, grafik, GIF ve MP4 katmanları için canlı önizleme.
- Arka plan GIF/MP4 yükleme ve uygulama akışı.
- Katman listesinde sıra, tür, veri kaynağı, metin, medya, konum, boyut, yazı tipi, kalınlık, renk ve format alanları.
- Statik metin katmanları ekleme.
- CPU/GPU sıcaklığı, yük, saat hızları, fan/pompa verisi, saat, tarih ve gün gibi canlı veri katmanları ekleme.
- Görsel katmanları ekleme.
- L-Connect'in modüler grafik stillerinden grafik katmanları ekleme.
- Desteklenen katmanlarda grafik stili, konum, boyut, renk ve veri kaynağı düzenleme.
- Çizim sırasını yönetmek için katmanları yukarı ve aşağı taşıma.
- Gölge katmanları ekleme ve hangi katmana bağlı olduklarını takip etme.
- Gölgenin hareketini ve rengini ana katmanla eşitleme.
- ARGB/HEX değerleriyle saydam renk desteği.
- `Y-M-D`, `D-M-Y`, `D.M.Y`, `00:00`, `00:00:00` ve AM/PM gibi tarih ve saat formatları.
- Çok dilli arayüz: İngilizce, Türkçe, Rusça ve Basitleştirilmiş Çince.
- Açık/koyu arayüz teması seçimi.
- Fan kontrol servisinin yeniden başlatılmasına gerek kalmadan şablon değişikliklerini kaydetmek ve L-Connect'i yeniletmek için `Apply All` akışı.
- `ps2exe` ile isteğe bağlı EXE derleme desteği.

## Desteklenen Cihazlar

| Cihaz | Durum | Notlar |
| --- | --- | --- |
| Hydroshift II LCD-S | Destekleniyor | Kare LCD önizlemesi. |
| Hydroshift II LCD-C | Destekleniyor | Yuvarlak önizleme maskesi. Yuvarlak ekrana özel şablon ve modüler öğeleri kullanır. |

Editör, mümkün olduğunda eksik ProgramData şablon, modüler öğe, tema ve önizleme dosyalarını kurulu L-Connect `Assets` klasöründen tamamlayabilir.

## Kurulum

1. Release sayfasını açın:
   <https://github.com/ozgurce/LianliThemeEditor/releases>
2. `EXE.zip` dosyasını indirin.
3. Arşivi çıkarın.
4. Çıkardıktan sonra klasör yapısı şöyle olmalıdır:

```text
ThemeEditor.exe
supporter.exe
lang/
  en.json
  ru.json
  tr.json
  zh.json
```

5. `ThemeEditor.exe` dosyasını çalıştırın.

## Sistem Gereksinimleri

- Windows 10 veya Windows 11.
- Kurulu L-Connect 3.
- PowerShell 5.1 veya daha yeni sürüm.
- Windows PowerShell üzerinden .NET/WPF desteği.
- `C:\ProgramData\Lian-Li\L-Connect 3` içine yazmak için yönetici yetkisi önerilir.
- Arka plan video/GIF hazırlama ve önizleme işlemleri için `ffmpeg` önerilir.
- İsteğe bağlı: PowerShell scriptlerinden bağımsız `.exe` oluşturmak için `ps2exe`.

`EXE/` klasöründen çalıştırıldığında editör gereken kaynakları `.exe` yanında ve gerektiğinde proje üst klasöründe arayacak şekilde tasarlanmıştır.

## PowerShell İle Çalıştırma

PowerShell açıp şunu çalıştırın:

```powershell
powershell -ExecutionPolicy Bypass -File editor.ps1
```

Projeyi farklı bir klasöre koyduysanız `editor.ps1` yolunu buna göre değiştirin.

## EXE Derleme

`ps2exe` yükleyin veya içe aktarın, ardından komutları proje klasöründe çalıştırın. Hazır release `.exe` dosyaları zaten `EXE.zip` içinde bulunduğu için bu bölüm yalnızca uygulamayı kendisi derlemek isteyenler içindir.

## Temel Kullanım

1. L-Connect 3'ü açın ve desteklenen LCD cihazı seçin.
2. L-Connect içinde bir şablon seçin veya aktif şablonu olduğu gibi bırakın.
3. `ThemeEditor.exe` dosyasını çalıştırın.
4. Editörde cihaz tipini seçin:
   - `Hydroshift II LCD-S`
   - `Hydroshift II LCD-C`
5. `Use active template` seçeneğini açık bırakın veya şablon ID'sini elle girin.
6. `Load` düğmesine basın.
7. Katmanı katman listesinden veya doğrudan önizleme alanından seçin.
8. Konum, yazı tipi, veri kaynağı, metin, boyut, renk, format veya grafik ayarlarını düzenleyin.
9. Seçili katman için `Apply` düğmesine basın.
10. Değişiklikleri kaydetmek ve L-Connect'i yeniletmek için `Apply All` düğmesini kullanın.

## Katman Türleri

Yaygın katman türleri:

- `GraphAnimation`: video, GIF veya görsel için arka plan katmanı.
- `GraphItem`: metin veya veri metni katmanı.
- `GraphImage`: görsel katmanı.
- `GraphStatuBar`: çizgisel ilerleme veya durum çubuğu.
- `GraphArchBar`: dairesel veya yay biçimli grafik.
- `GraphLine`: görev yöneticisindeki grafiklere benzeyen akış çizgisi grafiği.
- `GraphDynamicBar`: dinamik parçalı grafik veya bar öğesi.

Her L-Connect grafik nesnesinde her özellik bulunmaz. Editör yalnızca seçili katmanın desteklediği ayarları gösterir ve uygular.

## Veri Kaynakları

Editör yalnızca L-Connect şablonlarının gerçekten kullanabildiği veya gösterebildiği pratik veri kaynaklarını tutar.

- `CPUTEMP`: işlemci sıcaklığı.
- `CPUCLOCK`: işlemci saat hızı.
- `CPULOAD`: işlemci yükü.
- `CPUFAN`: L-Connect/HWiNFO tarafından işlemci fanı olarak görülen fan hızı. Bazı sistemlerde bu değer olmayabilir veya farklı adlandırılabilir.
- `GPUTEMP`: ekran kartı sıcaklığı.
- `GPUCLOCK`: ekran kartı saat hızı.
- `GPULOAD`: ekran kartı yükü.
- `RAMLOAD`: bellek kullanımı.
- `DRVLOAD`: disk kullanımı.
- `WATERPUMP`: pompa hızı.
- `TIME`: güncel saat.
- `DATE`: güncel tarih.
- `DAY`: haftanın günü. Gösterim L-Connect'in format ve davranışına bağlıdır.
- `APM`: 12 saatlik saat formatı için AM/PM göstergesi.
- `StaticText`: statik metin.

Bazı değerler donanıma, L-Connect sürümüne ve erişilebilir sensörlere bağlıdır.

## Tarih ve Saat Formatları

Saat formatı örnekleri:

```text
00:00
00:00:00
```

Tarih formatı örnekleri:

```text
Y-M-D
D-M-Y
D.M.Y
M
D
```

Tarih ve saat katmanları dinamik kalmalıdır. Bunlar normal statik metin olarak kaydedilmek için tasarlanmamıştır.

## Arka Plan Medyası

Editör arka plan GIF/MP4 seçimini destekler. Arka plan uygulandığında yardımcı modül, L-Connect'in yüklenen arka plan medyalarını saklama yöntemini taklit etmeye çalışır.

Faydalı yollar:

```text
C:\ProgramData\Lian-Li\L-Connect 3\uploaded
C:\ProgramData\Lian-Li\L-Connect 3\hydroshift-ii-lcd-s
C:\ProgramData\Lian-Li\L-Connect 3\hydroshift-ii-lcd-c
```

Editör normal uygulama işlemlerinde L-Connect servisini yeniden başlatmaktan kaçınır; bu servis fan ve pompa davranışını da etkileyebilir.

## Dil Desteği

Dil dosyaları şu klasördedir:

```text
lang/en.json
lang/tr.json
lang/ru.json
lang/zh.json
```

Bir arayüz metni çeviride yoksa veya hâlâ doğrudan koda yazılmış görünüyorsa tüm JSON dil dosyalarına eklenmeli ve `editor.ps1` içindeki yerelleştirme yardımcısı üzerinden bağlanmalıdır.

## Ayarlar

Yerel editör ayarları burada tutulur:

```text
theme_editor_settings.json
```

Bu dosya şunları içerebilir:

- seçili dil;
- seçili arayüz teması;
- seçili cihaz modeli;
- gölge katman bağlantıları.

Temiz release paylaşırken yalnızca kendi sisteminize ait kişisel ayarları göndermeyin.

## Sorun Giderme

### Arka plan uygulandı ama önizleme farklı medya gösteriyor

Şablonun L-Connect profilinde özel arka planı olup olmadığını kontrol edin. Yardımcı modül özel arka plan yollarını seçili cihaz modeline göre filtreler, çünkü bazı şablon ID'leri hem LCD-S hem LCD-C ailesinde bulunabilir.

### Erişim reddedildi

PowerShell'i veya `.exe` dosyasını yönetici olarak çalıştırın. L-Connect şablonları `C:\ProgramData` altında tuttuğu için buraya yazmak yükseltilmiş yetki gerektirebilir.

### Başlangıçta yanlış dil, tema veya cihaz görünüyor

Son release sürümünü kullanın. `0.985` sürümünde, başlangıçtaki programatik seçimlerin kayıtlı ayarları değiştirebilmesine neden olan sorun düzeltilmiştir.

## Geliştirici Notları

- `editor.ps1` WPF arayüzünü ve ana kullanıcı akışını içerir.
- `supporter.ps1` L-Connect şablonları ve profilleri üzerinde düşük seviye işlemleri yapar.
- Editör yanında varsa `supporter.exe` dosyasını tercih eder; yoksa `supporter.ps1` kullanır.
- Cihaza özel dosyalar hem `ProgramData` hem de L-Connect `Assets` klasöründe aranır.
- LCD-C, varsayılan şablonlar için aynı nesne modelini kullanır; ancak önizleme yuvarlak maske ile ele alınmalıdır.
- L-Connect'teki bazı özel tema ve profil verileri normal `GraphList` katmanlarından ayrı saklanır.

## Sorumluluk Reddi

Kendi sorumluluğunuzda kullanın. L-Connect şablonlarını düzenlemeden önce her zaman yedek alın. Fan ve pompa kontrolü L-Connect servisleri tarafından yönetildiği için LCD tema testi yaparken gereksiz servis yeniden başlatmalarından kaçının.
