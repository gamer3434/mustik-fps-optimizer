using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AntigravityFPSOptimizer
{
    public partial class MainWindow : Window
    {
        // For drag-move and window actions
        private DispatcherTimer? _statsTimer;
        private FILETIME _prevIdleTime;
        private FILETIME _prevKernelTime;
        private FILETIME _prevUserTime;

        // RAM API Import
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        public MainWindow()
        {
            InitializeComponent();
            
            // Check Admin Rights
            if (!Optimizer.IsAdministrator())
            {
                MessageBox.Show("Lütfen programı yönetici olarak (Run as Administrator) çalıştırın. Kernel düzeyindeki optimizasyonların uygulanması için yönetici yetkisi gereklidir.", "Yönetici Yetkisi Gerekli", MessageBoxButton.OK, MessageBoxImage.Warning);
                Application.Current.Shutdown();
                return;
            }

            // Init performance times
            GetSystemTimes(out _prevIdleTime, out _prevKernelTime, out _prevUserTime);

            // Initialize Timer for Stats (CPU/RAM)
            _statsTimer = new DispatcherTimer();
            _statsTimer.Interval = TimeSpan.FromSeconds(1);
            _statsTimer.Tick += StatsTimer_Tick;
            _statsTimer.Start();

            // Load specs
            LoadSystemSpecs();

            // Setup tweaks check list
            LoadTweaksUI();

            // Check Activation
            if (!LicensingManager.CheckActivationState())
            {
                ActivationOverlay.Visibility = Visibility.Visible;
                HwidDisplayBox.Text = LicensingManager.GetHardwareId();
            }
            else
            {
                CheckAndSetupAdminUI();
            }

            // Stop background RAM cleaner when GUI opens to avoid double cleaning/conflicts
            Program.StopBackgroundRamCleaner();

            // Load background RAM cleaning toggle setting
            BgRamCleanerToggle.IsChecked = Program.IsBgRamCleanerEnabled();

            // Hook window closing event to launch background cleaner
            this.Closing += MainWindow_Closing;

            // Trigger online status indicator checking
            StartOnlineStatusCheck();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Program.IsBgRamCleanerEnabled())
            {
                Program.StartBackgroundRamCleaner();
            }
            Application.Current.Shutdown();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        // Timer Tick: Update RAM and CPU in real time
        private void StatsTimer_Tick(object? sender, EventArgs e)
        {
            UpdateCpuUsage();
            UpdateRamUsage();
        }

        private void UpdateCpuUsage()
        {
            if (GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime))
            {
                ulong idleDiff = SubtractTime(idleTime, _prevIdleTime);
                ulong kernelDiff = SubtractTime(kernelTime, _prevKernelTime);
                ulong userDiff = SubtractTime(userTime, _prevUserTime);

                ulong sysDiff = kernelDiff + userDiff;

                if (sysDiff > 0)
                {
                    double cpuPercent = 100.0 - ((double)idleDiff * 100.0 / sysDiff);
                    if (cpuPercent < 0) cpuPercent = 0;
                    if (cpuPercent > 100) cpuPercent = 100;

                    CpuGauge.Value = Math.Round(cpuPercent);
                    CpuText.Text = $"{Math.Round(cpuPercent)}%";
                }

                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;
            }
        }

        private ulong SubtractTime(FILETIME a, FILETIME b)
        {
            ulong aVal = ((ulong)a.dwHighDateTime << 32) | (uint)a.dwLowDateTime;
            ulong bVal = ((ulong)b.dwHighDateTime << 32) | (uint)b.dwLowDateTime;
            return aVal - bVal;
        }

        private void UpdateRamUsage()
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                double usedRamGb = (memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
                double totalRamGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);

                RamGauge.Value = memStatus.dwMemoryLoad;
                RamText.Text = $"{memStatus.dwMemoryLoad}%";
                RamDetailText.Text = $"{usedRamGb:F1} GB / {totalRamGb:F1} GB";
            }
        }

        private void LoadSystemSpecs()
        {
            try
            {
                // CPU Info
                string cpuName = "Bilinmeyen İşlemci";
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("ProcessorNameString");
                        if (val != null) cpuName = val.ToString()?.Trim() ?? cpuName;
                    }
                }
                CpuSpecText.Text = cpuName;

                // GPU Info (Get GPU using Registry/Management if possible, fallback to Graphics drivers)
                string gpuName = "Yüksek Performanslı Grafik Kartı";
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WinSAT"))
                    {
                        var val = key?.GetValue("PrimaryAdapterString");
                        if (val != null) gpuName = val.ToString()!;
                    }
                }
                catch { }
                GpuSpecText.Text = gpuName;

                // OS Info
                string osName = Environment.OSVersion.VersionString;
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        var prod = key?.GetValue("ProductName");
                        var displayVersion = key?.GetValue("DisplayVersion");
                        if (prod != null)
                        {
                            osName = prod.ToString() + (displayVersion != null ? $" {displayVersion}" : "");
                        }
                    }
                }
                catch { }
                OsSpecText.Text = osName;

                // Memory Info
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    double totalRamGb = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    RamSpecText.Text = $"{totalRamGb:F1} GB RAM";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load system specs: {ex.Message}");
            }
        }

        private void LoadTweaksUI()
        {
            TweaksContainer.Children.Clear();
            foreach (var tweak in Optimizer.Tweaks)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(15),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
                    BorderThickness = new Thickness(1)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var textStack = new StackPanel();
                textStack.Children.Add(new TextBlock
                {
                    Text = tweak.Name,
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 15,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                textStack.Children.Add(new TextBlock
                {
                    Text = tweak.Description,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 550
                });

                Grid.SetColumn(textStack, 0);
                grid.Children.Add(textStack);

                // Custom Checkbox/Toggle style in code
                var toggle = new CheckBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)FindResource("ModernToggleStyle"),
                    Tag = tweak.Id,
                    IsChecked = tweak.IsAppliedCheck()
                };

                toggle.Checked += (s, e) => ApplyTweakById(tweak.Id, true);
                toggle.Unchecked += (s, e) => ApplyTweakById(tweak.Id, false);

                Grid.SetColumn(toggle, 1);
                grid.Children.Add(toggle);

                border.Child = grid;
                TweaksContainer.Children.Add(border);
            }
        }

        private void ApplyTweakById(string id, bool apply)
        {
            var tweak = Optimizer.Tweaks.FirstOrDefault(t => t.Id == id);
            if (tweak != null)
            {
                try
                {
                    if (apply)
                        tweak.ApplyAction();
                    else
                        tweak.RestoreAction();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"{tweak.Name} işlemi sırasında bir hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Navigation Tabs click handlers
        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton)
            {
                // Reset all button backgrounds/borders if any
                DashboardTab.Visibility = Visibility.Collapsed;
                TweaksTab.Visibility = Visibility.Collapsed;
                CleanerTab.Visibility = Visibility.Collapsed;
                RestoreTab.Visibility = Visibility.Collapsed;
                AdminTab.Visibility = Visibility.Collapsed;

                // Highlight active button and show tab
                string tabName = clickedButton.Tag.ToString() ?? "";
                switch (tabName)
                {
                    case "Dashboard":
                        DashboardTab.Visibility = Visibility.Visible;
                        break;
                    case "Tweaks":
                        TweaksTab.Visibility = Visibility.Visible;
                        // Refresh status of checks
                        LoadTweaksUI();
                        break;
                    case "Cleaner":
                        CleanerTab.Visibility = Visibility.Visible;
                        break;
                    case "Restore":
                        RestoreTab.Visibility = Visibility.Visible;
                        break;
                    case "Admin":
                        AdminTab.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        // --- BUTTON INTERACTION LOGICS ---

        // One-Click Boost on Dashboard
        private async void QuickBoostButton_Click(object sender, RoutedEventArgs e)
        {
            QuickBoostButton.IsEnabled = false;
            QuickBoostText.Text = "OPTIMIZE EDILIYOR...";
            ProgressBarGlow.Visibility = Visibility.Visible;

            await Task.Run(() =>
            {
                // Apply all registry tweaks
                foreach (var tweak in Optimizer.Tweaks)
                {
                    try { tweak.ApplyAction(); } catch { }
                }

                // Clear memory
                Optimizer.ClearStandbyList();

                // Clean Temp files
                Optimizer.CleanTempFiles();
            });

            ProgressBarGlow.Visibility = Visibility.Collapsed;
            QuickBoostText.Text = "HIZLANDIRILDI!";
            
            // Refresh tweaks view
            LoadTweaksUI();

            MessageBox.Show("Sistem optimizasyonları başarıyla uygulandı!\n\n- CPU Öncelikleri Düzenlendi\n- Bellek ve Standby Listesi Boşaltıldı\n- Disk Temizliği Yapıldı\n- Ping Optimizasyonu Sağlandı\n\nEn iyi performans için bilgisayarınızı yeniden başlatmanız önerilir.", "Hızlı Optimizasyon Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
            
            QuickBoostButton.IsEnabled = true;
            QuickBoostText.Text = "HIZLANDIR";
        }

        // Manual RAM Clean
        private async void CleanRamButton_Click(object sender, RoutedEventArgs e)
        {
            CleanRamButton.IsEnabled = false;
            RamCleanStatus.Text = "Bellek temizleniyor...";

            long freedBytes = 0;
            await Task.Run(() =>
            {
                freedBytes = Optimizer.ClearStandbyList();
            });

            double freedMb = freedBytes / (1024.0 * 1024.0);
            RamCleanStatus.Text = $"Başarıyla {freedMb:F1} MB RAM temizlendi!";
            UpdateRamUsage();

            CleanRamButton.IsEnabled = true;
        }

        // Manual Junk Cleaner
        private async void CleanJunkButton_Click(object sender, RoutedEventArgs e)
        {
            CleanJunkButton.IsEnabled = false;
            JunkCleanStatus.Text = "Gereksiz dosyalar taranıyor ve siliniyor...";

            long freedBytes = 0;
            await Task.Run(() =>
            {
                freedBytes = Optimizer.CleanTempFiles();
            });

            double freedMb = freedBytes / (1024.0 * 1024.0);
            JunkCleanStatus.Text = $"Başarıyla {freedMb:F1} MB gereksiz dosya temizlendi!";

            CleanJunkButton.IsEnabled = true;
        }

        // Create System Restore Point
        private async void CreateRestorePointButton_Click(object sender, RoutedEventArgs e)
        {
            CreateRestorePointButton.IsEnabled = false;
            RestoreStatusText.Text = "Sistem Geri Yükleme Noktası oluşturuluyor (Bu işlem birkaç dakika sürebilir)...";

            bool success = false;
            await Task.Run(() =>
            {
                success = Optimizer.CreateRestorePoint();
            });

            if (success)
            {
                RestoreStatusText.Text = "Sistem Geri Yükleme Noktası başarıyla oluşturuldu.";
                MessageBox.Show("Geri yükleme noktası oluşturuldu. Herhangi bir sorunda Windows Sistem Geri Yükleme aracını kullanarak bu ana dönebilirsiniz.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                RestoreStatusText.Text = "Geri Yükleme Noktası oluşturulamadı. Lütfen Windows Geri Yükleme Korumasının açık olduğunu kontrol edin.";
                MessageBox.Show("Restore Point oluşturulamadı. Windows Ayarlarından Sistem Koruması özelliğinin aktif olup olmadığını kontrol ediniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            CreateRestorePointButton.IsEnabled = true;
        }

        // Restore Defaults
        private async void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Yapılan tüm optimizasyon ayarlarını Windows varsayılan ayarlarına geri döndürmek istediğinizden emin misiniz?", "Varsayılana Geri Dön", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            RestoreDefaultsButton.IsEnabled = false;
            RestoreStatusText.Text = "Ayarlar varsayılana döndürülüyor...";

            await Task.Run(() =>
            {
                foreach (var tweak in Optimizer.Tweaks)
                {
                    try { tweak.RestoreAction(); } catch { }
                }
            });

            RestoreStatusText.Text = "Tüm ayarlar başarıyla orijinal haline geri yüklendi.";
            LoadTweaksUI();
            
            MessageBox.Show("Tüm optimizasyon ayarları başarıyla varsayılan Windows değerlerine döndürüldü. Değişikliklerin tamamen etkinleşmesi için bilgisayarınızı yeniden başlatın.", "Varsayılan Ayarlar Yüklendi", MessageBoxButton.OK, MessageBoxImage.Information);

            RestoreDefaultsButton.IsEnabled = true;
        }

        private void ProfileRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio)
            {
                switch (radio.Name)
                {
                    case "RadioCompetitive":
                        Optimizer.ActiveProfile = Optimizer.OptimizationProfile.Competitive;
                        break;
                    case "RadioStory":
                        Optimizer.ActiveProfile = Optimizer.OptimizationProfile.Story;
                        break;
                    case "RadioBalanced":
                        Optimizer.ActiveProfile = Optimizer.OptimizationProfile.Balanced;
                        break;
                }
            }
        }

        private void LicenseInputBox_GotFocus(object sender, RoutedEventArgs e)
        {
            LicenseWatermark.Visibility = Visibility.Collapsed;
        }

        private void LicenseInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LicenseInputBox.Text))
            {
                LicenseWatermark.Visibility = Visibility.Visible;
            }
        }

        private async void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            string key = LicenseInputBox.Text;
            if (string.IsNullOrWhiteSpace(key))
            {
                ActivationStatusText.Text = "Lisans anahtarı boş bırakılamaz!";
                ActivationStatusText.Foreground = Brushes.Red;
                return;
            }

            ActivateButton.IsEnabled = false;
            ActivationStatusText.Text = "Lisans doğrulanıyor...";
            ActivationStatusText.Foreground = Brushes.Cyan;

            await Task.Delay(1000); // 1 sec professional delay

            var result = LicensingManager.TryActivate(key);

            if (result == LicensingManager.ActivationResult.Success)
            {
                ActivationStatusText.Text = "Lisans başarıyla aktifleştirildi!";
                ActivationStatusText.Foreground = Brushes.Green;

                await Task.Delay(1000);
                ActivationOverlay.Visibility = Visibility.Collapsed;
                
                CheckAndSetupAdminUI();

                MessageBox.Show("Tebrikler! Lisansınız başarıyla doğrulandı ve bu bilgisayarın anakartına kilitlendi. İyi oyunlar!", "Lisans Aktif", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (result == LicensingManager.ActivationResult.AdminSuccess)
            {
                ActivationStatusText.Text = "Geliştirici Lisansı Doğrulandı!";
                ActivationStatusText.Foreground = Brushes.Green;

                await Task.Delay(1000);
                ActivationOverlay.Visibility = Visibility.Collapsed;
                
                CheckAndSetupAdminUI();

                MessageBox.Show("Hoş geldiniz Admin! Geliştirici anahtarınız başarıyla doğrulandı.", "Admin Girişi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (result == LicensingManager.ActivationResult.Expired)
            {
                ActivationStatusText.Text = "HATA: Bu keyin kullanım süresi dolmuştur!";
                ActivationStatusText.Foreground = Brushes.Red;
            }
            else if (result == LicensingManager.ActivationResult.LockedToOtherHwid)
            {
                ActivationStatusText.Text = "HATA: Bu key başka bir anakarta kilitlenmiştir!";
                ActivationStatusText.Foreground = Brushes.Red;
            }
            else if (result == LicensingManager.ActivationResult.NoInternet)
            {
                ActivationStatusText.Text = "HATA: İnternet bağlantısı yok veya sunucuya erişilemedi!";
                ActivationStatusText.Foreground = Brushes.Red;
            }
            else
            {
                ActivationStatusText.Text = "HATA: Geçersiz veya hatalı lisans anahtarı!";
                ActivationStatusText.Foreground = Brushes.Red;
            }

            ActivateButton.IsEnabled = true;
        }

        private void CheckAndSetupAdminUI()
        {
            string activeKey = LicensingManager.GetActiveKey();
            bool isAdmin = LicensingManager.IsAdminKey(activeKey);
            if (isAdmin)
            {
                AdminTabButton.Visibility = Visibility.Visible;
            }
            else
            {
                AdminTabButton.Visibility = Visibility.Collapsed;
            }
        }

        private void ChangeLicenseButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Aktif lisans anahtarını silmek ve farklı bir lisans anahtarı girmek istediğinizden emin misiniz?", "Lisansı Değiştir", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                LicensingManager.DeleteCachedLicense();
                LicenseInputBox.Text = string.Empty;
                LicenseWatermark.Visibility = Visibility.Visible;
                ActivationStatusText.Text = string.Empty;
                HwidDisplayBox.Text = LicensingManager.GetHardwareId();
                ActivationOverlay.Visibility = Visibility.Visible;
                
                // Hide admin tab on license reset
                AdminTabButton.Visibility = Visibility.Collapsed;
                if (AdminTab.Visibility == Visibility.Visible)
                {
                    AdminTab.Visibility = Visibility.Collapsed;
                    DashboardTab.Visibility = Visibility.Visible;
                }

                MessageBox.Show("Aktif lisans başarıyla sıfırlandı. Yeni lisans anahtarı girebilirsiniz.", "Lisans Sıfırlandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void GenerateKeyButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateKeyButton.IsEnabled = false;
            GeneratedKeyStatusText.Text = "Yeni lisans anahtarı bulut sunucusunda oluşturuluyor...";
            GeneratedKeyStatusText.Foreground = Brushes.Cyan;
            GeneratedKeyContainer.Visibility = Visibility.Collapsed;

            int durationDays = 1;
            if (Radio30Day.IsChecked == true) durationDays = 30;
            else if (RadioUnlim.IsChecked == true) durationDays = 99999;

            string generatedKey = string.Empty;
            await Task.Run(() =>
            {
                generatedKey = LicensingManager.GenerateNewKey(durationDays);
            });

            if (generatedKey.StartsWith("HATA"))
            {
                GeneratedKeyStatusText.Text = generatedKey;
                GeneratedKeyStatusText.Foreground = Brushes.Red;
            }
            else
            {
                GeneratedKeyStatusText.Text = "Lisans anahtarı başarıyla oluşturuldu ve buluta yüklendi!";
                GeneratedKeyStatusText.Foreground = Brushes.Green;
                GeneratedKeyBox.Text = generatedKey;
                GeneratedKeyContainer.Visibility = Visibility.Visible;
            }

            GenerateKeyButton.IsEnabled = true;
        }

        private void CopyKeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(GeneratedKeyBox.Text))
            {
                Clipboard.SetText(GeneratedKeyBox.Text);
                MessageBox.Show("Lisans anahtarı başarıyla panoya kopyalandı!", "Kopyalandı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (Program.IsBgRamCleanerEnabled())
            {
                Program.StartBackgroundRamCleaner();
            }
        }

        private void BgRamCleanerToggle_Checked(object sender, RoutedEventArgs e)
        {
            Program.SetBgRamCleanerEnabled(true);
        }

        private void BgRamCleanerToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            Program.SetBgRamCleanerEnabled(false);
            Program.StopBackgroundRamCleaner();
        }

        private void StartOnlineStatusCheck()
        {
            Task.Run(async () =>
            {
                bool isOnline = false;
                try
                {
                    using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                    {
                        // Direct ping check to the database
                        var response = await client.GetAsync("https://mustik-fps-licensing-default-rtdb.firebaseio.com/.json?shallow=true");
                        isOnline = response.IsSuccessStatusCode;
                    }
                }
                catch { }

                Dispatcher.Invoke(() =>
                {
                    if (isOnline)
                    {
                        // Glowing cyan-green for active online connection
                        OnlineIndicatorDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0xCC));
                        OnlineIndicatorGlow.Color = System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0xCC);
                        OnlineIndicatorText.Text = "Lisans Sunucusu: Çevrimiçi";
                        OnlineIndicatorText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0xCC));
                    }
                    else
                    {
                        // Glowing neon red for offline status
                        OnlineIndicatorDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x33, 0x66));
                        OnlineIndicatorGlow.Color = System.Windows.Media.Color.FromRgb(0xFF, 0x33, 0x66);
                        OnlineIndicatorText.Text = "Lisans Sunucusu: Bağlantı Yok";
                        OnlineIndicatorText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x33, 0x66));
                    }
                });
            });
        }

        private void DiscordLink_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://guns.lol/mustik34") { UseShellExecute = true });
            }
            catch { }
        }
    }
}







