# Masaustu Dolabi - Proje Rehberi

## Mimari ve kararlar
- Kullanici onayli stack: C# .NET 8, WPF, SQLite. Core veri ve baslatma davranisi korunur.
- Controls/CabinetScene.cs gercek Viewport3D sahnesini olusturur. Viewport2DVisual3D ile kategori ve kisayol dugmeleri fiziksel yuzeylere yerlestirilir; mevcut MainWindow olaylari kullanilir.
- Controls/CabinetMesh.cs govde, ray, tepsi, kulp ve blok cikintilarinin geometrisini ve malzemelerini olusturur.
- Themes/Physical.xaml yeni cekmece etiketleri ve kaydirma cubugu; Shared.xaml ortak kisayol kartlari. Eski DrawerTray yeni ana sahnede kullanilmaz.
- Acilma Transform3D uzerinden yapilir; pencere ve diger cekmeceler yeniden dizilmez. Kesilen animasyon mevcut konumundan devam eder. Azaltilmis hareket anlik uygulanir.
- Sol/sag yerlestirme kamerayi ve acilma yonunu degistirir, yazilari aynalamaz. Tema degisimi kategori kimliklerini ve acik kategoriyi korur.
- Uretilmis PNG dokular elle degistirilmedi. Yeni metal yuzey kodla olusturulan DrawingBrush kullanir.

## Dogrulama
```
dotnet build src/Dolap.App
dotnet test tests/Dolap.Core.Tests
MasaustuDolabi.exe --design-preview
MasaustuDolabi.exe --visual-check <cikti-klasoru>
MasaustuDolabi.exe --app-smoke <cikti-klasoru>
```

DesignPreview ornek koleksiyonla sahneyi kontrol eder. VisualSmoke bellek ici store ile gercek MainWindow olaylarini, ayarlari, modal akisini ve saydam pikselleri kontrol eder. Eksik dosya hedefiyle baslatma hata yolu denenir; gercek kullanici dosyasi acilmaz. Bu modlar kullanici DB'sini, startup kaydini ve global kisayollarini degistirmez.

2026-10-25: derleme temiz; 13 Core, 7 sahne ve 12 ana pencere kontrolu gecti. Bunlar paket/kurulum, coklu monitor/DPI, ekran okuyucu veya performans kabul testi yerine gecmez. Gercek zamanli 3D gorunum konsept yonunu uygular; konsept resimleriyle piksel eslesmesi iddia edilmez.

Urun sahibi: Umut Celik. Uygulamanin Hakkinda ekranindaki X/GitHub baglantilari korunur.
