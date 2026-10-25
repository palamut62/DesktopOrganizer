# DURUM - Masaustu Dolabi
guncel: 2026-10-25 | son arac: Claude Code (Opus 5.5) | dal: (git yok -> main) | asama: 1 + 2 + 3 kismen

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
- [ ] Gorsel kalite: konsepte yaklastir (derinlik/perspektif, gercek 3D yok), tepsi dokusu, ikon 64px kalitesi, uygulama exe ikonu
- [ ] Dogrulanmadi: coklu DPI/monitor, Win+D, ekran okuyucu, performans butceleri (AT-07..AT-18), otomatik gizle, kurulum paketi
- [ ] Surukle-birak ile oge/kategori siralama (simdilik menuden sola/saga, yukari/asagi)
- [ ] Qt karari kesinlesirse tema katmani yeniden yazilir (Core degismez)
## Acik sorunlar
- Otomatik UI testinde (SetCursorPos) pencere beklenmedik tasindi; elle surukleme/ayar ile karisti, kesinlesmedi. Baslik tiklamasi artik hareket yoksa ayar kaydetmez.
## Dokunulmayacak
- PRD kararlari; kaynak dosyalara yazilmaz; native dialog yok.
