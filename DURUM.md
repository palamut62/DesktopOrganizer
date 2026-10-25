# DURUM - Masaustu Dolabi
Guncel: 2026-10-25 | son arac: Claude Code | asama: teslim tamamlandi, iyilestirme yol haritasi

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

## Iyilestirme yol haritasi (henuz uygulanmadi)
Bu liste beyin firtinasi fikirlerinin tamamidir; tamamlandi anlamina gelmez. Kullanici devam edilmesini istedi. Ilk uygulama paketi P1 yerlesimdir.
- [x] P1 Manyetik hizalama: ekran kenarlari ve diger dolaplara yapisma, tutarli bosluk (Alt ile atla). LayoutSnap + testler.
- [x] P1 Yerlesimi kilitleme: tasima/dondurme kilidi (Ayarlar > Yerlesim).
- [x] P2 Klasore bagli cekmece: kategori menusu > Klasore bagla; FileSystemWatcher (800 ms) ust duzeyi yansitir, en fazla 200 oge; dosyalara dokunmaz; sema v4 categories.linked_folder.
- [ ] P3 Calisma setleri: Is / Yazilim / Oyun gibi dolap gorunum duzenleri arasinda gecis.
- [ ] P4 Hizli onizleme: bosluk tusuyla resim, PDF ve metin onizlemesi.
- [ ] P5 Dolap tasarim editoru: govde/cekmece rengi, malzeme, kulp ve etiket secimi.
- [ ] Akilli cekmeceler: son indirilenler, son acilanlar, bu hafta degisenler.
- [ ] Toplu baslatma: secili uygulama, klasor ve siteleri tek hareketle acma.
- [ ] Kirik kisayol tespiti ve hedefi yeniden eslestirme.
- [ ] Farkli dolap modulleri: ince evrak, genis takim, iki cekmeceli modul.
- [ ] Kapali cekmecede icerik ipucu: kucuk simgeler ve uzerine gelince onizleme (mevcut oge sayisina ek).
- [ ] Malzemeye ozel hareket ayari; mevcut ses ac/kapa yanina ses seviyesi ve hareket siddeti.
- [ ] Gecici birakma tepsisi: dosya, ekran goruntusu ve baglantilari sonradan dagitma.
- [ ] Cekmece ici boluculer / alt sekmeler.
- [ ] Proje cekmecesi: klasor, terminal, editor, siteler ve notlar; toplu baslatmayla birlikte.
- [ ] Duzen zaman makinesi: yerlesim gecmisi ve geri donme (mevcut geri al/yedekten daha genis).
- [ ] Tasinabilir duzen: baska bilgisayarda eksik yollarin yeniden eslestirilmesi (mevcut JSON aktarimina ek).

## Korunacak kararlar
- Dosyalara referans veren kisayollar; hedef dosyalara dokunulmaz.
- Hakkinda: Urun sahibi Umut Celik ve tiklanabilir X/GitHub.
- Native alert/confirm yok; uygulama ici modal/toast.
