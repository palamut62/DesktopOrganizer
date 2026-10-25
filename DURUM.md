# DURUM - Masaustu Dolabi
guncel: 2026-10-25 | son arac: Claude Code | asama: 1 + 2 + 3 kismen, 3D gorsel katman yenilendi

## Ne
PRD'ye gore Windows 11 cekmece-kisayol uygulamasi. WPF (.NET 8). Karar: Qt Quick 3D yerine WPF (Qt/CMake kurulu degil, lisans karari acik).

## Calistir / test
- `dotnet build src/Dolap.App` ; exe: src/Dolap.App/bin/Debug/net8.0-windows/MasaustuDolabi.exe (`--open 1` = 2. cekmece acik, ekran goruntusu icin)
- `dotnet test tests/Dolap.Core.Tests` (13 test gecer)
- Veri: %LOCALAPPDATA%\MasaustuDolabi\dolap.db (+ backups\)
- Dokular: `python tools/gen_assets.py` -> src/Dolap.App/Assets

## Yapilanlar
- [x] Core: SQLite store (kategori/kisayol CRUD, sira, tasima, geri al, ayar, yedek, JSON disa/ice aktarma), Turkce arama katlama, hedef siniflandirma
- [x] App: seffaf pencere, 2 tema (malzeme dokulu), cekmece animasyonu (kesilebilir), tepsi, global kisayol (Ctrl+Alt+D), tek ornek, Windows ile baslat, ayarlar penceresi (5 sekme, Hakkinda'da urun sahibi), arama (Ctrl+F), surukle-birak, masaustu ice aktarma, ilk acilis
- [x] Baslatma: ShellExecute, hata siniflari Turkce, 1 sn tekrar engeli
## Siradaki
- [x] Gercek WPF 3D: sabit govde, hareketli cekmece/tepsi/ray, metal kulp, blok cikintilari; sol/sag perspektif, tema degisiminde koleksiyon ve acik cekmece korunur.
- [ ] Uygulama EXE ikonu; farkli GPU/DPI ve uzun kullanim gorsel kontrolu.
- [ ] Dogrulanmadi: coklu DPI/monitor, Win+D, ekran okuyucu, performans butceleri (AT-07..AT-18), otomatik gizle, kurulum paketi
- [ ] Surukle-birak ile oge/kategori siralama (simdilik menuden sola/saga, yukari/asagi)
- [ ] Yeni kaynaklarin paketlenmesi ve kurulu surume teslimi (kullanici onayi bekleniyor).
## Son oturum (Claude Code, coklu dolap)
- Core sema v2: `cabinets` tablosu (ad, tema, kenar, monitor, olcek, serbest konum, otomatik gizle, ustte, acilma yonu); kategoriler `cabinet_id` ile dolaba bagli. v1 DB yukseltmeden once yedeklenir, tek varsayilan dolaba doner. En fazla 6 dolap; son dolap silinemez; silinen dolabin kategorileri tasinir.
- App: her dolap ayri MainWindow (kendi tema/kenar/yon/olcek/monitor); global kisayol ilk dolapta, hepsini acar/kapatir; Ayarlar'da dolap secici + Yeni/Sil/Ad + Acilma yonu; kategori baglam menusu 'Baska dolaba tasi'; tepsi 'Yeni dolap ekle'. Core 20 test, app-smoke 12/12 gecti.
- Bilinen sinir: Application kaynaklari (eski Themes/*.xaml) tek tema tasir; 3D sahne dolap basina dogru temayi kullanir, ortak gorsel stil son uygulanan temadir.
- Ses (cekmece acilma/kapanma): Codex'e birakildi.

## Gorsel kalite (Claude Code)
- Cekmece ici kisayollar sekmeli karton dosya klasoru (Shared.xaml ItemButtonStyle): icinden kagit gorunur, ikon onde, ad etiket cikartmasinda, hover'da yukari kalkar.
- 3D yuzeylere basilan 2D katmanlar 3x (dusuk kalitede 1.5x) cozunurlukte cizilir (CabinetScene.TextureScale); kabuk ikonlari 256 px istenir.
- visual-check 7/7, app-smoke 12/12, Core 20/20.

## Dik evrak + duvar kagidi (Claude Code)
- Cekmece ici: kisayollar masaustu gibi gercek kabuk ikonu + golgeli ad; 3D'de dik duran kartlar (4 sutun, arka siralar yuksek). 1. tik kaldirir, 2. tik acar; klavye odagi kaldirir, Enter acar. 12+ oge yukari uzar (sayfalama yok).
- Duvar kagidi: tools/gen_wallpapers.py -> src/Dolap.App/Wallpapers/{metal,blocks}-{right,left}.jpg (3840x2400). Ilk dolabin tema+yonune gore uygulanir; orijinal %LOCALAPPDATA%\MasaustuDolabi\wallpaper-original.txt'de saklanir, ayar kapatilinca geri yuklenir. PRD 'duvar kagidi degismez' karari kullanici istegiyle degisti.
- Kaldirma/kurulum paketi yok: uygulama silinirse duvar kagidi geri yuklenmez (elle: ayari kapat).

## Gorsel duzeltme (frontend-design ilkeleri)
- Kapali cekmecelerin kart cerceveleri dolap ustunde hayalet karo gibi gorunuyordu: SetOpen artik karti (Button.Parent) gizliyor.
- Seritteki uzun talimat metni kaldirildi (yalniz bos durumda yonlendirme); basliktaki izli buyuk harf METAL/BLOK etiketi kaldirildi; kartlar ince cam, siralar cekmece icinde kalacak sekilde alcaltildi.

## Doku + ses + kurulum
- 3B malzemeler: tools/gen_assets.py tex_brushed_steel.png / tex_abs_plastic.png (1024x512). Blok rengi DiffuseMaterial.Color+AmbientColor ile dokuya carpilir. Viewport3D HighQuality filtre.
- Sesler: tools/gen_sounds.py -> Sounds/{metal,blocks}-{open,close}.wav (sentez; yalniz surtunme/ray kayma sesi, tik-mandal-carpma yok, 0.25-0.4 sn). Ayar: Davranis > Cekmece acilma ve kapanma sesi (varsayilan acik). Test/onizlemede sessiz.
- Kurulum: dotnet publish -c Release -r win-x64 --self-contained false -o %LOCALAPPDATA%\Programs\MasaustuDolabi + Baslat menusu kisayolu. Kaldirici yok.

## 3B dondurme + son durum (sema v3)
- cabinets: yaw, pitch, open_category. Baslikta dondurme tutamaci (surukle, cift tik sifirla, sol/sag ok 5 derece); Ctrl+surukle her yerde. Sinir yaw +-55, pitch +-25 (on yuz okunur kalsin). Birakinca kaydedilir.
- Acilista son konum (serbest surukleme zaten kayitli), aci, tema ve son acik cekmece gelir; disari tiklamayla kapanma kaydedilmez.
- Core 22 test, visual-check (metal-rotated, blocks-rotated dahil), app-smoke 12/12. Kurulu surum guncellendi.

## Dondurme iyilestirme + yerlesim + golge + arama
- Turntable dondurme (dikey eksen sabit, pivot dolap merkezi), hedefe yumusak yaklasma + birakinca momentum (CompositionTarget.Rendering yalniz hareket varken), donus araliginda sabit kadraj (olcek pompalamasi yok).
- Hareketler: govdeyi sol surukle, her yerde sag surukle (4px alti baglam menusu), Ctrl+sol surukle; baslikta 4 yon oku (RepeatButton, basili tut), ok tusu, cift tik sifirlar.
- Pencere sahne boyunda (min 460 DIP), dolap alta yaslanir: zemine kadar indirilebilir; kenara yapisinca dikey konum korunur.
- Cekmece acilirken yana kayma (OffsetX) kaldirildi: bosluk olusmuyor, cekmece dumduz cikar.
- Zemin temas golgesi (radyal, dusuk kalitede hafif). Arama: Kapat dugmesi, ara dugmesi ac/kapa, disari tiklama kapatir.
- app-smoke ilk soguk calismada bir kez cekmece-acilma zamanlamasinda dustu, sonraki 3 calisma gecti (zamanlama hassas).

## Ok tuslari + olcek + golge duzeltmeleri
- Hata: hizli art arda tik ok dugmesinde cift-tik sifirlamasini tetikliyordu; yon kameraya gore tanimliydi. Simdi oklar ve surukleme dolabin kendisini cevirir (nesne semantigi), cift-tik sifirlama kaldirildi; sifirlama Ayarlar > Yerlesim > Aciyi sifirla. Ayni ayar yeniden uygulaninca sahne yeniden kurulmaz (basili ok kesilmiyordu). DesignPreview'e 3 regresyon kontrolu eklendi.
- Olcek: sahne dunya birimi basina sabit 1.2 DIP (PixelsPerUnit); kadraj tum acilarin ekran-uzayi birlesimi (asimetrik), cekilmis cekmece yalniz +-20 derecede dahil. BottomSlack kadar pencere ekran altina tasabilir.
- Baslik: yon oklari solda yuvarlak dugmeler, ara/ekle/ayarlar sagda.
- Golge: iki katmanli zemin golgesi; duvar kagidindaki sabit golge kaldirildi, zemin biraz aydinlatildi (golge okunsun).

## Gorev cubugu
- Uygulama yalniz sistem tepsisinde: dolap pencereleri WS_EX_TOOLWINDOW, Ayarlar penceresi de ShowInTaskbar=False (Ayarlar tepsi menusunden acilir). Process MainWindowHandle=0 ile dogrulandi.

## Cekmece kutusu
- Cekmece kutusu govdenin arka panelinden uzundu (z -198 < -180): dolap cevrilince arka/yandan tasiyordu. Kutu derinligi 168, Travel 218 -> 150 (CabinetScene.Travel, testler buna bagli); acikken arka duvar govde icinde kalir.

## Masaustu toplama
- Kisayollar klasorundeki 27 .lnk cekmecelere dagitildi (referans; dosyalar yerinde): Gelistirme, Is, Medya, yeni Oyun, Iletisim, Bulut ve Guvenlik, Sistem (Bu Bilgisayar / Geri Donusum / Denetim Masasi = explorer.exe shell:::{CLSID}). Masaustu klasorleri Is/Arsiv cekmecelerinde. Oncesi yedek: backups/oncesi-masaustu-*.db.
- Ayar HideDesktopIcons (Gorunum): Explorer DefView WM_COMMAND 0x7402 ile 'Masaustu simgelerini goster' kapatilir; Cikis'ta geri acilir. Dolap 3'te bos 'Is' cekmecesi duruyor (kullanicinin, silinmedi).

## Temaya uygun oda + canli hava
- Oda ilk dolabin temasini izler: blocks -> LEGO tuglali duvar, stud'lu taban plakasi, kirmizi pencere, sari stud'lu esik; metal -> brut beton panel, cilali beton zemin, siyah celik fabrika penceresi (base-{theme}-...).
- Kose golgesi bolge sinirlarindan (yan duvarlara bant tasmaz); gunes izi zeminde, duvarin onunde, pencere bicimli ve sola kayik.
- Canli hava: Windows konum hizmeti (net8.0-windows10.0.19041.0, Geolocator), izin yoksa sehir (Tunceli). Son durum %LOCALAPPDATA%/MasaustuDolabi/hava-durumu.txt.
- Ofis odasinda dolap govdesi zemin yamuguna sigdirilir (KeepOnFloor), ayak hizasi zemin cizgisinin altinda kalir.
- Bilinen: gunes izi cita deseni iki tema icin ortak (2x2); metal pencere 4x3.

## Ofis odasi (duvar kagidi)
- tools/gen_office.py -> src/Dolap.App/Office (oda gunduz/gece x pencereli/pencresiz, 12 gokyuzu: acik/bulutlu/yagmur/kar x gunduz/alacakaranlik/gece + sehir silueti, tavan isigi, gunes izi). OfficeRoom.Render calisma aninda birlestirir, %LOCALAPPDATA%/office-<surum>-<durum>.jpg onbellek.
- OfficeLogic (Core, testli): WMO kodu -> gokyuzu; gun dogumu/batimi +-40 dk alacakaranlik; tavan isigi otomatik/acik/kapali. Hava yoksa mevsimsel tahmin.
- WeatherService: Open-Meteo geocoding + forecast (anahtarsiz), 30 dk onbellek; App.RefreshRoom 10 dk'da bir. Ayarlar > Oda: oda stili, pencere, isiklar, hava + sehir (varsayilan kapali; sehir girilince acilir).
- Zemin golgesi 3B zemin yuzeyi yerine 2B izdusum (goz hizasinda kayboluyordu). Core 37 test.

## Sistem monitoru + duz gorunum + derinlik + surukle-sirala
- MonitorWindow (duvardaki TV): CPU (GetSystemTimes), RAM (GlobalMemoryStatusEx), GPU (PDH GPU Engine 3D, 4 ornekte bir), disk (DriveInfo, 15 ornekte bir), en yogun 6 surec (3 ornekte bir), 2 sn; gizliyken olcmez. Ayarlar > Monitor: goster, boyut (%60-160), cerceve (Koyu/Ahsap/Gumus/Neon); konum suruklenir ve kaydedilir. Tepside ac/kapa.
- Bosta yuksek CPU olcumu: kaynak uygulama degil, dis UI Automation istemcisi (codex-computer-use vb.) arayuz agacini sorguluyor (Dispatcher kancasiyla dogrulandi). Profil kodu kaldirildi.
- Varsayilan bakis tam karsidan, goz hizasina yakin 6 derece (ust yuzey ince serit); arka duvarda yumusak temas golgesi (dolap duvara yasli). Smoke/visual-check soguk baslangic zamanlamasina karsi saglamlastirildi. Cekmece onu 113->121 (araliklar 3 birim).
- DepthScale: FreeY artik ayak hizasi; ufuk (ekranin %70'i) yakininda 0.6x, ekran altinda ~1.05x. Ayarlar > Yerlesim'de kapatilabilir.
- Kart surukle-birak: 8px sonra baslar, baska kartin uzerine birak = onun yerine gecer; cekmece onune birak = o cekmeceye tasinir (dosyaya dokunmaz). Gercek fareyle test edilmedi.

## Onceki oturum
- Eski duz akordeon yerine CabinetScene / CabinetMesh ve Physical.xaml kullaniliyor. WPF karari korundu.
- Derleme: 0 hata, 0 uyari; Core 13/13 test; izole sahne 7/7, gercek MainWindow 12/12 kontrol gecti.
- `--design-preview`: ornek verili guvenli gorsel onizleme. `--visual-check <klasor>` ve `--app-smoke <klasor>`: PNG ve kontrol raporlari.
- Testler kullanici veritabanini, baslangic kaydini ve global kisayollarini degistirmez. Kurulu uygulama guncellenmedi.
## Acik sorunlar
- Otomatik UI testinde (SetCursorPos) pencere beklenmedik tasindi; elle surukleme/ayar ile karisti, kesinlesmedi. Baslik tiklamasi artik hareket yoksa ayar kaydetmez.
## Dokunulmayacak
- PRD kararlari; kaynak dosyalara yazilmaz; native dialog yok.
