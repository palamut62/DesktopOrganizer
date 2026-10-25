# DURUM - Masaustu Dolabi
Guncel: 2026-10-25 | son arac: Codex | dal: main | asama: inceleme duzeltmeleri teslim edildi

## Proje
- C# .NET 8 WPF, SQLite. Kullanici onayli stack korunur.
- Veri: %LOCALAPPDATA%/MasaustuDolabi/dolap.db; yedekler backups/.
- En fazla 6 dolap, dolap basina tema, konum, yon, monitor, olcek, yaw/pitch ve acik cekmece saklanir.
- Tema: Metal / Blok; iki temaya ozel acilma-kapanma sesleri ve ses kapatma ayari mevcut.
- Sistem monitoru, masaustu simgelerini gizleme, arama, kisayol/kategori islemleri korunur.

## Son degisiklikler
- Oda sekmesi, oda/hava/konum servisleri, zamanlayici, oda varliklari ve ureticisi kaldirildi.
- Odaya ait konum kisitlari kaldirildi. Dikey konuma gore boyut ayari kullanicinin tercihi olarak korunur.
- Eski JSON oda alanlari yok sayilir; koleksiyon ve diger tercihler korunur. Teslimatta DB yedegi ve butunluk kontrolu yapildi.
- net8.0-windows hedefi; konum icin eklenen Windows SDK hedefi artik gerekmiyor.
- Metal/plastik dokular 2048x1024, ince ve dusuk kontrastli. PNG kaynaklari tools/gen_assets.py ile yeniden uretildi.
- Kutu yan/ust yuzey UV eslemesi duzeltildi; doku tek satira ezilip uzamiyor.
- Kulp ve stud silindirleri 48 dilimli, yan normalleri yumusak; metal parlama siddeti dengelendi.
- 3D uzerindeki yazi/ikon yuzeyleri normal modda 4x, dusuk kaynak modunda 1.5x orneklenir.
- tools/gen_wallpapers.py dogrudan 3840x2400 statik, dengeli isikli grafit arka plan uretir. Metal/blok varyantlari, sag/sol dosya uyumlulugu korunur.
- Uretilen AI konsepti 1586x992'dir; paketlenen 3840x2400 duvar kagitlari native prosedurel uretimdir, raster buyutme degil.

## Calistir / dogrula
- dotnet build src/Dolap.App
- dotnet test tests/Dolap.Core.Tests
- MasaustuDolabi.exe --design-preview
- MasaustuDolabi.exe --visual-check <cikti-klasoru>
- MasaustuDolabi.exe --app-smoke <cikti-klasoru>
- python tools/gen_wallpapers.py
- Derleme 0 hata/uyari; Core 23/23; ana pencere 13/13 kontrol gecti. Sahne/geometri 12/12 kontrol gecti.
- Python duvar kagidi betigi Ruff format/check temiz; 4 JPG 3840x2400 olarak dogrulandi.

## Teslimat ve kalan sinirlar
- Kullanici onayiyla Release win-x64 paket kurulu konuma sessizce kopyalandi; uygulama yeniden baslatildi. Kurulu DLL hash degeri uretim paketiyle ayni; 13/13 kurulu pencere kontrolu gecti.
- Surum 0.1.0 korundu. DB yedeklendi; 9 kategori / 37 kisayol ve integrity_check=ok dogrulandi. Onceki kurulum yerel work/installation-before-20261025 klasorunde saklandi. GitHub mevcut Release listesi bos; kaynaklar bu teslimat commitinde.
- Coklu DPI/monitor ve farkli GPU, ekran okuyucu, uzun sureli performans ve gercek fareyle kart siralama ayrica dogrulanmali.
- Onceki gelisim notlari PROJE-REHBER.md icinde tarihsel kayit olarak korundu.
- GitHub main push dogrulandi: 6cb8354934d7dc85ecc3844ba88f02ff2e13e46d.

## Iyilestirme yol haritasi (uygulandi)
Tum maddeler icin kod eklendi (sema v5); son incelemedeki 5 bulgu kaynakta duzeltildi. Dogrulama: Core 37/37, app-smoke 26/26, visual-check gecti. Gercek fareyle: tepsi surukle-birak, klasor secme ve proje sihirbazi ayrica denenmeli.
- [x] P1 Manyetik hizalama: ekran kenarlari ve diger dolaplara yapisma, tutarli bosluk (Alt ile atla). LayoutSnap + testler.
- [x] P1 Yerlesimi kilitleme: tasima/dondurme kilidi (Ayarlar > Yerlesim).
- [x] P2 Klasore bagli cekmece: kategori menusu > Klasore bagla; FileSystemWatcher (800 ms) ust duzeyi yansitir, en fazla 200 oge; dosyalara dokunmaz; sema v4 categories.linked_folder.
- [x] P3 Calisma setleri: Ayarlar > Duzenler ve tepsi menusu; konum/tema/aci/tasarim/gorunurluk adla kaydedilir (snapshots tablosu).
- [x] P4 Hizli onizleme: kartta Bosluk ya da menu > Onizle; resim, metin (ilk 64 KB), digerleri kabuk kucuk resmi (PDF ilk sayfa), klasor listesi.
- [x] P5 Dolap tasarim editoru: Ayarlar > Gorunum > Bu dolabin tasarimi (govde rengi, kulp: cubuk/topuz/girinti, etiket: plaka/duz).
- [x] Akilli cekmeceler: + > Son indirilenler / Son acilanlar / Bu hafta degisenler; 10 dk'da bir ve menuden guncellenir (linked_folder = smart:<tur>).
- [x] Toplu baslatma: cekmece menusu > Tumunu ac (6'dan fazlasinda onay, 180 ms arayla).
- [x] Kirik kisayol: kartta soluk + ipucu; Ayarlar > Veri > Kirik kisayollari bul ve onar (tek tek yeni konum / kaldir).
- [x] Dolap modulleri: standart / ince evrak / genis takim / buyuk cekmeceli (3B olcek + 2B yuzeyler oranli buyur, yazi esnemez).
- [x] Kapali cekmece ipucu: on yuzde ilk 5 ogenin kucuk simgeleri, uzerine gelince ilk 8 ogenin adi.
- [x] Hareket/ses: malzemeye gore sure (blok 290, metal 340 ms) x hareket hizi (0.5-1.6); ses seviyesi 0-100 (PCM olcekleme).
- [x] Birakma tepsisi: ayri pencere (Davranis ayari / tepsi menusu); surukle-birak, Ctrl+V (dosya, baglanti, ekran goruntusu -> tepsi/ klasorune PNG); menuden cekmeceye tasi.
- [x] Cekmece bolumleri: kart menusu > Bolum...; ayni bolum yan yana, basinda etiketli ayrac karti (shortcuts.section).
- [x] Proje cekmecesi: + > Proje cekmecesi; klasor, Windows Terminal (yoksa cmd), VS Code (varsa), notlar (uygulama klasorunde .md), siteler; Tumunu ac.
- [x] Duzen zaman makinesi: dakikada bir degisiklik varsa otomatik an (son 30); Ayarlar > Duzenler > Secili ana geri don (once mevcut hal kaydedilir).
- [x] Tasinabilir duzen: geri yuklemeden sonra kirik yol varsa onarim acilir; ortak onek onerisiyle toplu yol esleme (RemapPrefix).

## Inceleme duzeltmeleri (Codex, 2026-10-25)
- 5 bulgu giderildi: yedekten sonra pencereler yeni dolap kimlikleriyle kurulur; eski izleyiciler/zamanlayicilar kapatilir, ayarlar secicisi yenilenir.
- Yol esleme tam klasor/alt yol sinirini kullanir; kok ve UNC yollarini destekler. linked_folder da ayni transaction icinde eslenir; izleyici Reload ile yenilenir.
- JSON yedegi adli WorkSets alanini tasir. Eski yedekler okunur; koleksiyon degisiminde eski otomatik gecmis temizlenir (UI onayinda belirtilir).
- ShortcutHealth hem .lnk dosyasini hem yerel hedefini kontrol eder; shell/web hedeflerini dosya saymaz. Kart ve onarim ayni kurali kullanir.
- Tekrarli arayuz testi icin bagli-klasor fixture yolu benzersiz yapildi; onceki calismanin dosyalari testi etkilemez.
- Son dogrulama: derleme 0 hata/uyari, Core 42/42, ana pencere 26/26, geri yukleme 6/6. Release win-x64 kurulum sessizce guncellendi (0.1.0); kurulu EXE 26+6 kontrolu gecti, DLL SHA256 paketle ayni. DB ve eski kurulum yedeklendi; 3 dolap / 9 kategori / 37 kisayol korundu. GitHub Release bulunmuyor; kaynaklar bu teslimat commitinde main dalina aktarilir.
## Korunacak kararlar
- Dosyalara referans veren kisayollar; hedef dosyalara dokunulmaz.
- Hakkinda: Urun sahibi Umut Celik ve tiklanabilir X/GitHub.
- Native alert/confirm yok; uygulama ici modal/toast.
