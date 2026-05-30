using System;
using System.Linq;

namespace AntigravityFPSOptimizer
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Contains("--background-ram"))
            {
                RunBackgroundRamCleaner();
                return;
            }

            // If they pass "gui", start WPF App
            bool guiMode = args.Contains("gui") || args.Contains("-gui") || args.Contains("/gui");

            if (guiMode)
            {
                // Launch WPF App
                var app = new App();
                var mainWindow = new MainWindow();
                app.Run(mainWindow);
            }
            else
            {
                // Run Console mode directly by default!
                RunCommandLineInterface();
            }
        }

        private static void RunCommandLineInterface()
        {
            StopBackgroundRamCleaner();

            Console.Title = "Kernel FPS Artırma - Mustik Dev [CMD MODU]";
            Console.Clear();
            
            if (!Optimizer.IsAdministrator())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[HATA] Bu programi calistirmak icin Yonetici (Administrator) yetkileri gereklidir.");
                Console.WriteLine("Lutfen exe dosyasina SAG TIKLAYIP 'Yonetici Olarak Calistir' secenegine basin.");
                Console.ResetColor();
                Console.WriteLine("\nCikmak icin bir tusa basin...");
                Console.ReadKey();
                return;
            }

            // --- FIREBASE BAĞLANTI VE HATA ANALİZ TESTİ ---
            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[*] Firebase veritabanı bağlantısı test ediliyor...");
                Console.ResetColor();

                using (var testClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                {
                    // Force TLS 1.2/1.3
                    System.Net.ServicePointManager.SecurityProtocol = 
                        System.Net.SecurityProtocolType.Tls12 | 
                        System.Net.SecurityProtocolType.Tls13;

                    // Fetch licenses shallow query to verify both internet and read connectivity without generating noise or test keys in database
                    string testUrl = "https://mustik-fps-licensing-default-rtdb.firebaseio.com/licenses.json?shallow=true";
                    
                    var getResp = testClient.GetAsync(testUrl).GetAwaiter().GetResult();
                    if (getResp.IsSuccessStatusCode)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[✔] Veritabanı Okuma (GET) Başarılı! (Status: {getResp.StatusCode})");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[❌] Veritabanı Okuma Başarısız! (Status: {getResp.StatusCode})");
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[CRITICAL ERROR] FIREBASE BAĞLANTI HATASI!");
                Console.WriteLine($"Hata Mesajı: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Detay: {ex.InnerException.Message}");
                    if (ex.InnerException.InnerException != null)
                    {
                        Console.WriteLine($"İç Detay: {ex.InnerException.InnerException.Message}");
                    }
                }
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                Console.ResetColor();
                Console.WriteLine("\nDevam etmek için bir tuşa basın...");
                Console.ReadKey();
            }

            if (!LicensingManager.CheckActivationState())
            {
                RunLicensingInterface();
            }

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"==================================================================");
            Console.WriteLine(@"  _  __                     _   ______ _____   _____              ");
            Console.WriteLine(@" | |/ /                    | | |  ____|  __ \ / ____|             ");
            Console.WriteLine(@" | ' / ___ _ __ _ __   ___| | | |__  | |__) | (___               ");
            Console.WriteLine(@" |  < / _ \ '__| '_ \ / _ \ | |  __| |  ___/ \___ \              ");
            Console.WriteLine(@" | . \  __/ |  | | | |  __/ | | |    | |     ____) |             ");
            Console.WriteLine(@" |_|\_\___|_|  |_| |_|\___|_| |_|    |_|    |_____/              ");
            Console.WriteLine(@"                                                                  ");
            Console.WriteLine(@"      [  MUSTIK DEV  -  KERNEL LEVEL FPS OPTIMIZER CLI v2  ]       ");
            
            string activeKey = LicensingManager.GetActiveKey();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"      [ LISANSLI KOPYA: {activeKey} - ANAKART KILITLI ]      ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"==================================================================");
            Console.ResetColor();
            bool isAdmin = activeKey.Equals(LicensingManager.AdminMasterKey, StringComparison.OrdinalIgnoreCase);

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n--- PERFORMANS PROFILLERI ---");
                Console.ResetColor();
                Console.WriteLine("[1] Rekabetçi Mod (CS2 & Valorant - En Düşük ms)");
                Console.WriteLine("[2] Hikaye Modu (Grafik & GPU Yoğun)");
                Console.WriteLine("[3] Dengeli Mod (Standart)");
                Console.WriteLine("[4] Yapilan Tum Ayarlari Geri Al");
                Console.WriteLine("[5] RAM & Disk Çöpü Temizliği Yap");
                
                string ramCleanerText = _isContinuousRamCleaningActive 
                    ? "Sürekli RAM Temizleyiciyi DURDUR [AKTIF]" 
                    : "Sürekli RAM Temizleyiciyi Başlat (Arka Planda Sürekli Çalışır)";
                Console.ForegroundColor = _isContinuousRamCleaningActive ? ConsoleColor.Green : ConsoleColor.Gray;
                Console.WriteLine($"[6] {ramCleanerText}");
                Console.ResetColor();
                
                Console.WriteLine("[7] Lisans Değiştir (Farklı Key Gir)");
                if (isAdmin)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine("[8] Admin Key Oluşturma Paneli");
                    Console.ResetColor();
                    Console.WriteLine("[9] Çıkış");
                }
                else
                {
                    Console.WriteLine("[8] Çıkış");
                }

                string menuRange = isAdmin ? "1-9" : "1-8";
                Console.Write($"\nLutfen bir secim yapin [{menuRange}]: ");
                string? choice = Console.ReadLine();

                if (!isAdmin && choice == "8") break;
                if (isAdmin && choice == "9") break;

                switch (choice)
                {
                    case "1":
                        Optimizer.ActiveProfile = Optimizer.OptimizationProfile.Competitive;
                        ApplyAllTweaksConsole();
                        break;
                    case "2":
                        Optimizer.ActiveProfile = Optimizer.OptimizationProfile.Story;
                        ApplyAllTweaksConsole();
                        break;
                    case "3":
                        Optimizer.ActiveProfile = Optimizer.OptimizationProfile.Balanced;
                        ApplyAllTweaksConsole();
                        break;
                    case "4":
                        RestoreAllTweaksConsole();
                        break;
                    case "5":
                        CleanSystemConsole();
                        break;
                    case "6":
                        ToggleContinuousRamCleaner();
                        break;
                    case "7":
                        ChangeLicenseConsole();
                        return;
                    case "8":
                        if (isAdmin)
                        {
                            RunAdminPanelInterface();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("\n[!] Gecersiz secim.");
                            Console.ResetColor();
                            System.Threading.Thread.Sleep(1000);
                        }
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n[!] Gecersiz secim. Lutfen {menuRange} arasinda bir sayi girin.");
                        Console.ResetColor();
                        break;
                }
            }

            if (IsBgRamCleanerEnabled())
            {
                StartBackgroundRamCleaner();
            }
        }

        private static void ApplyAllTweaksConsole()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[*] {Optimizer.ActiveProfile} profili secildi. Optimizasyonlar uygulaniyor...");
            Console.ResetColor();

            foreach (var tweak in Optimizer.Tweaks)
            {
                try
                {
                    tweak.ApplyAction();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[+] Basariyla uygulandi: {tweak.Name}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[-] Hata olustu ({tweak.Name}): {ex.Message}");
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[✔] Sistem ve Kernel optimizasyonlari basariyla uygulandi!");
            Console.WriteLine("[✔] En iyi performans icin bilgisayarinizi yeniden baslatmaniz onerilir.");
            Console.ResetColor();
        }

        private static void RestoreAllTweaksConsole()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[*] Tum ayarlar varsayilana donduruluyor...");
            Console.ResetColor();

            foreach (var tweak in Optimizer.Tweaks)
            {
                try
                {
                    tweak.RestoreAction();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[+] Orijinal haline getirildi: {tweak.Name}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[-] Hata: {ex.Message}");
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[✔] Tum ayarlar orijinal Windows varsayilanlarina geri yuklendi!");
            Console.ResetColor();
        }

        private static void CleanSystemConsole()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[*] RAM ve gereksiz dosyalar temizleniyor...");
            Console.ResetColor();

            long freedRam = Optimizer.ClearStandbyList();
            double freedRamMb = freedRam / (1024.0 * 1024.0);
            Console.WriteLine($"[+] RAM Önbelleği temizlendi: {freedRamMb:F1} MB RAM serbest birakildi.");

            long freedJunk = Optimizer.CleanTempFiles();
            double freedJunkMb = freedJunk / (1024.0 * 1024.0);
            Console.WriteLine($"[+] Gereksiz sistem dosyalari silindi: {freedJunkMb:F1} MB alan temizlendi.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[✔] Hızlı temizlik basariyla tamamlandi!");
            Console.ResetColor();
        }

        private static volatile bool _isContinuousRamCleaningActive = false;

        private static string BgRamConfigPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AntigravityFPSOptimizer",
            "bgram.cfg"
        );

        public static bool IsBgRamCleanerEnabled()
        {
            try
            {
                if (System.IO.File.Exists(BgRamConfigPath))
                {
                    return System.IO.File.ReadAllText(BgRamConfigPath).Trim() == "1";
                }
            }
            catch { }
            return false;
        }

        public static void SetBgRamCleanerEnabled(bool enabled)
        {
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(BgRamConfigPath);
                if (dir != null && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(BgRamConfigPath, enabled ? "1" : "0");
            }
            catch { }
        }

        public static void StartBackgroundRamCleaner()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath, "--background-ram")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch { }
        }

        public static void StopBackgroundRamCleaner()
        {
            try
            {
                using (var exitEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, "AntigravityBackgroundRamExitEvent"))
                {
                    exitEvent.Set();
                }
            }
            catch { }
        }

        private static void RunBackgroundRamCleaner()
        {
            using (var mutex = new System.Threading.Mutex(true, "AntigravityBackgroundRamCleanerMutex", out bool createdNew))
            {
                if (!createdNew) return;

                using (var exitEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, "AntigravityBackgroundRamExitEvent"))
                {
                    exitEvent.Reset();

                    while (true)
                    {
                        try
                        {
                            Optimizer.ClearStandbyList();
                        }
                        catch { }

                        // Sleep/wait for 30 seconds or wake up if requested to exit
                        if (exitEvent.WaitOne(30000))
                        {
                            break;
                        }
                    }
                }
            }
        }

        private static void ToggleContinuousRamCleaner()
        {
            if (_isContinuousRamCleaningActive)
            {
                _isContinuousRamCleaningActive = false;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[!] Sürekli RAM temizleyici durduruldu.");
                Console.ResetColor();
            }
            else
            {
                _isContinuousRamCleaningActive = true;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[✔] Sürekli RAM temizleyici arka planda başlatıldı! (Her 15 saniyede bir otomatik temizler)");
                Console.ResetColor();
                System.Threading.Tasks.Task.Run(RunContinuousRamCleaner);
            }
        }

        private static async System.Threading.Tasks.Task RunContinuousRamCleaner()
        {
            while (_isContinuousRamCleaningActive)
            {
                await System.Threading.Tasks.Task.Delay(15000); // Check every 15 seconds

                if (!_isContinuousRamCleaningActive) break;

                long freedRam = Optimizer.ClearStandbyList();
                if (freedRam > 1048576) // Only print if at least 1MB is cleared (1024 * 1024)
                {
                    double freedMb = freedRam / (1024.0 * 1024.0);
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.WriteLine($"\n[OTOMATİK RAM TEMİZLEME] {freedMb:F1} MB RAM önbelleği arka planda başarıyla temizlendi!");
                    Console.ResetColor();
                    Console.Write("\nLutfen bir secim yapin [1-7]: ");
                }
            }
        }

        private static void RunLicensingInterface()
        {
            while (true)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine(@"==================================================================");
                Console.WriteLine(@"        _      _____ _____ ______ _   _  _____ _____ _   _  _____ ");
                Console.WriteLine(@"       | |    |_   _/ ____|  ____| \ | |/ ____|_   _| \ | |/ ____|");
                Console.WriteLine(@"       | |      | || |    | |__  |  \| | (___   | | |  \| | |  __ ");
                Console.WriteLine(@"       | |      | || |    |  __| | . ` |\___ \  | | | . ` | | |_ |");
                Console.WriteLine(@"       | |____ _| || |____| |____| |\  |____) |_| |_| |\  | |__| |");
                Console.WriteLine(@"       |______|_____\_____|______|_| \_|_____/|_____|_| \_|\_____|");
                Console.WriteLine(@"                                                                  ");
                Console.WriteLine(@"              [  LISANS DOGRULAMA & ANAKART KILIDI  ]             ");
                Console.WriteLine(@"==================================================================");
                Console.ResetColor();

                string hwid = LicensingManager.GetHardwareId();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n[+] BILGISAYARINIZIN LISANS ANAKART ID'SI (HWID):");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    {hwid}");
                Console.ResetColor();

                Console.WriteLine("\n------------------------------------------------------------------");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(" * Bu program HWID lisanslama sistemi kullanir.");
                Console.WriteLine(" * Gireceginiz key dogrudan bu bilgisayarin ANAKARTINA kilitlenecektir.");
                Console.WriteLine(" * Denemek icin VIP key: MUSTIK-VIP-5090-RTX veya MUSTIK-FREE-TEST-KEY");
                Console.ResetColor();
                Console.WriteLine("------------------------------------------------------------------");

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("\nLutfen gecerli bir Lisans Key girin: ");
                string? inputKey = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(inputKey))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[!] Lisans anahtari bos birakilamaz!");
                    Console.ResetColor();
                    Console.WriteLine("\nYeniden denemek icin bir tusa basin...");
                    Console.ReadKey();
                    continue;
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n[*] Lisans anahtari veritabanindan sorgulaniyor...");
                System.Threading.Thread.Sleep(1000); // 1 sec professional verification delay

                var result = LicensingManager.TryActivate(inputKey);

                if (result == LicensingManager.ActivationResult.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[✔] TEBRIKLER! Lisans basariyla dogrulandi ve ANAKARTINIZA kilitlendi!");
                    Console.WriteLine("    Key kalici olarak bu bilgisayarla eslestirildi.");
                    Console.ResetColor();
                    Console.WriteLine("\nFPS Optimizer uygulamasina girmek icin bir tusa basin...");
                    Console.ReadKey();
                    break;
                }
                else if (result == LicensingManager.ActivationResult.AdminSuccess)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[✔] HOS GELDINIZ ADMIN! Master Developer anahtari basariyla dogrulandi!");
                    Console.ResetColor();
                    Console.WriteLine("\nDeveloper Yonetici Paneline girmek icin bir tusa basin...");
                    Console.ReadKey();
                    RunAdminPanelInterface();
                    break;
                }
                else if (result == LicensingManager.ActivationResult.Expired)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[HATA] Bu lisans anahtarinin kullanim suresi (Expiration) dolmus!");
                    Console.WriteLine("    Lutfen yeni bir lisans anahtari edinin.");
                    Console.ResetColor();
                    Console.WriteLine("\nYeniden denemek icin bir tusa basin...");
                    Console.ReadKey();
                }
                else if (result == LicensingManager.ActivationResult.LockedToOtherHwid)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[HATA] Bu lisans anahtari baska bir bilgisayara (ANAKARTA) kilitlenmis!");
                    Console.WriteLine("    Anakart kilidi devrededir. Lisans sahibiyle iletisime gecin.");
                    Console.ResetColor();
                    Console.WriteLine("\nYeniden denemek icin bir tusa basin...");
                    Console.ReadKey();
                }
                else if (result == LicensingManager.ActivationResult.NoInternet)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[HATA] Internet baglantisi bulunamadi veya sunucuya erisilemedi!");
                    Console.WriteLine("    Lutfen internet baglantinizi kontrol edip yeniden deneyin.");
                    Console.ResetColor();
                    Console.WriteLine("\nYeniden denemek icin bir tusa basin...");
                    Console.ReadKey();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[HATA] Gecersiz veya hatali lisans anahtari!");
                    Console.ResetColor();
                    Console.WriteLine("\nYeniden denemek icin bir tusa basin...");
                    Console.ReadKey();
                }
            }
        }

        private static void RunAdminPanelInterface()
        {
            while (true)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(@"==================================================================");
                Console.WriteLine(@"  __  __          _____ _______ ______ _____                      ");
                Console.WriteLine(@" |  \/  |   /\   / ____|__   __|  ____|  __ \                     ");
                Console.WriteLine(@" | \  / |  /  \ | (___    | |  | |__  | |__) |                    ");
                Console.WriteLine(@" | |\/| | / /\ \ \___ \   | |  |  __| |  _  /                     ");
                Console.WriteLine(@" | |  | |/ ____ \____) |  | |  | |____| | \ \                     ");
                Console.WriteLine(@" |_|  |_/_/    \_\_____/   |_|  |______|_|  \_\                   ");
                Console.WriteLine(@"                                                                  ");
                Console.WriteLine(@"           MUSTIK DEV - MASTER DEVELOPER KEY GENERATOR            ");
                Console.WriteLine(@"==================================================================");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n--- MASTER YONETICI KEY URETIM MENUSU ---");
                Console.ResetColor();
                Console.WriteLine("[1] 1 Günlük Key Üret (MDEV-1DAY-xxxx)");
                Console.WriteLine("[2] 30 Günlük Key Üret (MDEV-30DAY-xxxx)");
                Console.WriteLine("[3] Sınırsız VIP Key Üret (MDEV-UNLIM-xxxx)");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[4] FPS Optimizer Programina Giris Yap");
                Console.ResetColor();
                Console.WriteLine("[5] Cikis");

                Console.Write("\nLutfen bir secim yapin [1-5]: ");
                string? choice = Console.ReadLine();

                if (choice == "5") Environment.Exit(0);
                if (choice == "4")
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[*] FPS Optimizer ana programina yonlendiriliyorsunuz...");
                    System.Threading.Thread.Sleep(1000);
                    break;
                }

                int durationDays = 0;
                if (choice == "1") durationDays = 1;
                else if (choice == "2") durationDays = 30;
                else if (choice == "3") durationDays = 99999;
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[!] Gecersiz secim. Lutfen 1-5 arasinda bir sayi secin.");
                    Console.ResetColor();
                    Console.WriteLine("\nYeniden denemek icin bir tusa basin...");
                    Console.ReadKey();
                    continue;
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n[*] Yeni lisans anahtari bulut sunucusunda olusturuluyor...");
                
                string generatedKey = LicensingManager.GenerateNewKey(durationDays);
                
                if (generatedKey.StartsWith("HATA"))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[HATA] Anahtar olusturulamadi: {generatedKey}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[✔] ANAHTAR BASARIYLA OLUSTURULDU VE BULUTA YUKLENDI!");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"    Key: {generatedKey}");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"    Tip: {(durationDays == 99999 ? "VIP Sınırsız" : $"{durationDays} Günlük Geçici")}");
                    Console.ResetColor();
                }

                Console.WriteLine("\nDevam etmek icin bir tusa basin...");
                Console.ReadKey();
            }
        }

        private static void ChangeLicenseConsole()
        {
            LicensingManager.DeleteCachedLicense();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[✔] Lisans basariyla sifirlandi! Yeni lisans key giris ekranina yonlendiriliyorsunuz...");
            Console.ResetColor();
            System.Threading.Thread.Sleep(1500);
            RunCommandLineInterface();
        }
    }
}
