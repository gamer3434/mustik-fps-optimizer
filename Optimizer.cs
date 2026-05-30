using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Linq;

namespace AntigravityFPSOptimizer
{
    public class Tweak
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Action ApplyAction { get; set; } = () => { };
        public Action RestoreAction { get; set; } = () => { };
        public Func<bool> IsAppliedCheck { get; set; } = () => false;
    }

    public static class Optimizer
    {
        public enum OptimizationProfile
        {
            Competitive,
            Story,
            Balanced
        }

        public static OptimizationProfile ActiveProfile { get; set; } = OptimizationProfile.Competitive;

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProcess);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        public static List<Tweak> Tweaks { get; private set; } = new List<Tweak>();

        static Optimizer()
        {
            InitializeTweaks();
        }

        public static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void InitializeTweaks()
        {
            // --- 1. CPU / MMCSS Tweaks ---
            Tweaks.Add(new Tweak
            {
                Id = "cpu_mmcss",
                Name = "MMCSS Oyun Önceliklendirme",
                Category = "CPU & Zamanlayıcı",
                Description = "Windows Kernel'inin oyunlara CPU ve GPU önceliği tanımasını sağlar. Arka plan servislerinin oyunlarda anlık donmalara yol açmasını engeller.",
                ApplyAction = () =>
                {
                    int responsiveness = ActiveProfile == OptimizationProfile.Competitive ? 0 : 10;
                    int priorityVal = ActiveProfile == OptimizationProfile.Competitive ? 6 : 8; 
                    int gpuPriorityVal = ActiveProfile == OptimizationProfile.Competitive ? 8 : 18; 
                    string schedulingCat = "High";

                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                    {
                        key.SetValue("SystemResponsiveness", responsiveness, RegistryValueKind.DWord);
                        key.SetValue("NetworkThrottlingIndex", -1, RegistryValueKind.DWord);
                    }
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"))
                    {
                        key.SetValue("GPU Priority", gpuPriorityVal, RegistryValueKind.DWord);
                        key.SetValue("Priority", priorityVal, RegistryValueKind.DWord);
                        key.SetValue("Scheduling Category", schedulingCat, RegistryValueKind.String);
                        key.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                    }
                },
                RestoreAction = () =>
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true))
                    {
                        if (key != null)
                        {
                            key.SetValue("SystemResponsiveness", 14, RegistryValueKind.DWord);
                            key.SetValue("NetworkThrottlingIndex", 10, RegistryValueKind.DWord);
                        }
                    }
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true))
                    {
                        if (key != null)
                        {
                            key.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                            key.SetValue("Priority", 2, RegistryValueKind.DWord);
                            key.SetValue("Scheduling Category", "Medium", RegistryValueKind.String);
                            key.SetValue("SFIO Priority", "Normal", RegistryValueKind.String);
                        }
                    }
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                        {
                            if (key == null) return false;
                            var resp = key.GetValue("SystemResponsiveness");
                            var net = key.GetValue("NetworkThrottlingIndex");
                            if (resp == null || net == null || Convert.ToInt32(resp) != 0 || Convert.ToInt32(net) != -1)
                                return false;
                        }
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"))
                        {
                            if (key == null) return false;
                            var priority = key.GetValue("Priority");
                            var sched = key.GetValue("Scheduling Category");
                            return priority != null && Convert.ToInt32(priority) == 6 && sched != null && sched.ToString() == "High";
                        }
                    }
                    catch { return false; }
                }
            });

            // --- 2. CPU Core Parking & Ultimate Performance ---
            Tweaks.Add(new Tweak
            {
                Id = "cpu_power",
                Name = "Çekirdek Park Etmeyi Devre Dışı Bırak",
                Category = "CPU & Zamanlayıcı",
                Description = "CPU çekirdeklerinin boştayken uyku moduna (parking) geçmesini kapatır. CPU her zaman tam güçte ve aktif kalarak stuttering (takılma) riskini sıfıra indirir.",
                ApplyAction = () =>
                {
                    // Create Ultimate Performance scheme if it doesn't exist
                    RunCmd("powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                    // Set current scheme to maximize performance
                    RunCmd("powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 100");
                    RunCmd("powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMAXCORES 100");
                    // Disable core parking parameters in registry for general overrides
                    // Decouple core parking
                    RunCmd("powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR 5d76a2fa-e32a-478e-a90a-a2d652293f63 100");
                    RunCmd("powercfg -active SCHEME_CURRENT");
                },
                RestoreAction = () =>
                {
                    // Restore to Balanced plan (default)
                    RunCmd("powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 5");
                    RunCmd("powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMAXCORES 100");
                    RunCmd("powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR 5d76a2fa-e32a-478e-a90a-a2d652293f63 0");
                    RunCmd("powercfg -active SCHEME_CURRENT");
                },
                IsAppliedCheck = () =>
                {
                    // Checks if minimum processor state in powercfg registry is 100
                    try
                    {
                        var output = RunCmdOutput("powercfg -q SCHEME_CURRENT SUB_PROCESSOR CPMINCORES");
                        return output.Contains("0x00000064") || output.Contains("100"); // 64 in hex is 100
                    }
                    catch { return false; }
                }
            });

            // --- 3. GPU Hardware-Accelerated Scheduling (HAGS) ---
            Tweaks.Add(new Tweak
            {
                Id = "gpu_hags",
                Name = "Donanım Hızlandırmalı GPU Zamanlaması (HAGS)",
                Category = "Ekran Kartı (GPU)",
                Description = "Ekran kartının kendi belleğini doğrudan yönetmesini sağlar (HAGS). CPU yükünü azaltır, FPS değerini artırır ve gecikmeyi düşürür.",
                ApplyAction = () =>
                {
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                    {
                        key.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
                    }
                },
                RestoreAction = () =>
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true))
                    {
                        if (key != null)
                        {
                            key.SetValue("HwSchMode", 1, RegistryValueKind.DWord);
                        }
                    }
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                        {
                            if (key == null) return false;
                            var val = key.GetValue("HwSchMode");
                            return val != null && Convert.ToInt32(val) == 2;
                        }
                    }
                    catch { return false; }
                }
            });

            // --- 3.5 NVIDIA RTX Special Tweak ---
            Tweaks.Add(new Tweak
            {
                Id = "gpu_nvidia_rtx",
                Name = "NVIDIA RTX (5090/4090) Güç ve Gecikme Optimizasyonu",
                Category = "Ekran Kartı (GPU)",
                Description = "RTX 5090/4090 gibi yüksek performanslı NVIDIA kartları için Ansel, telemetri servislerini devre dışı bırakır ve grafik sürücü güç tasarruflarını kapatarak maksimum çekirdek ve bellek saat hızını korur.",
                ApplyAction = () =>
                {
                    // Disable CUDA Force P2 state (forces full memory clock speed during CUDA/GPU loads)
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                    {
                        key.SetValue("PowerSavingsBehavior", 1, RegistryValueKind.DWord);
                    }
                    // Disable NVIDIA Ansel
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\NVIDIA Corporation\Global\Ansel"))
                    {
                        key.SetValue("AnselEnabled", 0, RegistryValueKind.DWord);
                    }
                    // Disable NvTelemetryContainer service
                    RunCmd("sc config NvTelemetryContainer start= disabled");
                    RunCmd("sc stop NvTelemetryContainer");
                },
                RestoreAction = () =>
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true))
                    {
                        key?.DeleteValue("PowerSavingsBehavior", false);
                    }
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NVIDIA Corporation\Global\Ansel", true))
                    {
                        key?.SetValue("AnselEnabled", 1, RegistryValueKind.DWord);
                    }
                    RunCmd("sc config NvTelemetryContainer start= auto");
                    RunCmd("sc start NvTelemetryContainer");
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
                        {
                            if (key == null) return false;
                            var val = key.GetValue("PowerSavingsBehavior");
                            return val != null && Convert.ToInt32(val) == 1;
                        }
                    }
                    catch { return false; }
                }
            });

            // --- 4. Game Mode & Game Bar Disable ---
            Tweaks.Add(new Tweak
            {
                Id = "game_mode",
                Name = "Windows Oyun Modu & Game Bar",
                Category = "Ekran Kartı (GPU)",
                Description = "Windows Oyun Modu'nu aktif hale getirirken, oyunlarda arka planda çalışan ve FPS düşüren Game Bar kayıt alma özelliğini optimize eder.",
                ApplyAction = () =>
                {
                    // Enable Game Mode
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar"))
                    {
                        key.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                        key.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                    }
                    // Disable AppCapture (Game DVR Background recording)
                    using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                    {
                        key.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                    }
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR"))
                    {
                        key.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
                    }
                },
                RestoreAction = () =>
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", true))
                    {
                        if (key != null)
                        {
                            key.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                            key.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                        }
                    }
                    using (var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore", true))
                    {
                        if (key != null)
                        {
                            key.SetValue("GameDVR_Enabled", 1, RegistryValueKind.DWord);
                        }
                    }
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", true))
                    {
                        if (key != null)
                        {
                            key.DeleteValue("AllowGameDVR", false);
                        }
                    }
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore"))
                        {
                            if (key == null) return false;
                            var val = key.GetValue("GameDVR_Enabled");
                            return val != null && Convert.ToInt32(val) == 0;
                        }
                    }
                    catch { return false; }
                }
            });

            // --- 5. Network Ping / Nagle's Algorithm ---
            Tweaks.Add(new Tweak
            {
                Id = "net_latency",
                Name = "Ağ Gecikmesi & Ping Optimizasyonu (TCP)",
                Category = "Ağ ve Ping",
                Description = "TCP Nagle algoritmasını devre dışı bırakır. Ağ kartınız veri paketlerini hemen gönderir, böylece çevrimiçi oyunlarda anlık ping dalgalanmaları önlenir.",
                 ApplyAction = () =>
                {
                    // Global Network Card & TCP Stack latency optimizations
                    RunCmd("netsh int tcp set global rss=enabled");
                    RunCmd("netsh int tcp set global chimney=disabled");
                    RunCmd("netsh int tcp set global netdma=disabled");
                    RunCmd("netsh int tcp set global dca=enabled");
                    RunCmd("netsh int tcp set global autotuninglevel=normal");
                    RunCmd("netsh int tcp set global ecncapability=disabled");
                    RunCmd("netsh int tcp set global timestamps=disabled");

                    string interfacesPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
                    using (var interfacesKey = Registry.LocalMachine.OpenSubKey(interfacesPath, true))
                    {
                        if (interfacesKey == null) return;
                        foreach (string subkeyName in interfacesKey.GetSubKeyNames())
                        {
                            using (var interfaceKey = interfacesKey.OpenSubKey(subkeyName, true))
                            {
                                if (interfaceKey != null)
                                {
                                    if (ActiveProfile == OptimizationProfile.Story)
                                    {
                                        interfaceKey.DeleteValue("TcpAckFrequency", false);
                                        interfaceKey.DeleteValue("TCPNoDelay", false);
                                        interfaceKey.DeleteValue("TcpDelAckTicks", false);
                                    }
                                    else
                                    {
                                        interfaceKey.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                                        interfaceKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                                        interfaceKey.SetValue("TcpDelAckTicks", 0, RegistryValueKind.DWord);
                                    }
                                }
                            }
                        }
                    }
                },
                RestoreAction = () =>
                {
                    // Restore default TCP stack behavior
                    RunCmd("netsh int tcp set global rss=default");
                    RunCmd("netsh int tcp set global chimney=default");
                    RunCmd("netsh int tcp set global netdma=default");
                    RunCmd("netsh int tcp set global dca=default");
                    RunCmd("netsh int tcp set global autotuninglevel=normal");

                    string interfacesPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
                    using (var interfacesKey = Registry.LocalMachine.OpenSubKey(interfacesPath, true))
                    {
                        if (interfacesKey == null) return;
                        foreach (string subkeyName in interfacesKey.GetSubKeyNames())
                        {
                            using (var interfaceKey = interfacesKey.OpenSubKey(subkeyName, true))
                            {
                                if (interfaceKey != null)
                                {
                                    interfaceKey.DeleteValue("TcpAckFrequency", false);
                                    interfaceKey.DeleteValue("TCPNoDelay", false);
                                    interfaceKey.DeleteValue("TcpDelAckTicks", false);
                                }
                            }
                        }
                    }
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        string interfacesPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
                        using (var interfacesKey = Registry.LocalMachine.OpenSubKey(interfacesPath))
                        {
                            if (interfacesKey == null) return false;
                            foreach (string subkeyName in interfacesKey.GetSubKeyNames())
                            {
                                using (var interfaceKey = interfacesKey.OpenSubKey(subkeyName))
                                {
                                    if (interfaceKey != null)
                                    {
                                        var ack = interfaceKey.GetValue("TcpAckFrequency");
                                        var delay = interfaceKey.GetValue("TCPNoDelay");
                                        // If at least one active interface has it set to 1, we consider it applied
                                        if (ack != null && delay != null && Convert.ToInt32(ack) == 1 && Convert.ToInt32(delay) == 1)
                                            return true;
                                    }
                                }
                            }
                        }
                        return false;
                    }
                    catch { return false; }
                }
            });

            // --- 5.5 CPU-to-GPU Communication & DPC Latency Tweak ---
            Tweaks.Add(new Tweak
            {
                Id = "cpu_gpu_sync",
                Name = "İşlemci - Ekran Kartı İletişim & Gecikme Optimizasyonu",
                Category = "Sistem & Servisler",
                Description = "İşlemci ile Ekran Kartı arasındaki PCIe veriyolu tıkanıklığını çözer. DPC gecikmesini düşürmek için HPET'i (Yüksek Hassasiyetli Zamanlayıcı) devre dışı bırakır ve Windows prioritizasyonunu ön plana alır.",
                ApplyAction = () =>
                {
                    // Disable HPET in boot config to reduce DPC latency and CPU overhead
                    RunCmd("bcdedit /set useplatformclock no");
                    RunCmd("bcdedit /set disabledynamictick yes");

                    // Optimize Win32PrioritySeparation to give foreground games high quantum intervals
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl"))
                    {
                        // 0x26 (decimal 38) -> Short, Variable, High foreground boost quantum (Best for gaming)
                        key.SetValue("Win32PrioritySeparation", 38, RegistryValueKind.DWord);
                    }

                    // Large System Cache optimization for CPU/GPU memory translation pages minimums
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
                    {
                        key.SetValue("LargeSystemCache", 0, RegistryValueKind.DWord); // 0 ensures RAM is fully freed for gaming processes rather than system file cache
                        key.SetValue("IoPageLimit", 983040, RegistryValueKind.DWord); // Increases I/O page limits (0xF0000 = 983040)
                    }
                },
                RestoreAction = () =>
                {
                    RunCmd("bcdedit /deletevalue useplatformclock");
                    RunCmd("bcdedit /deletevalue disabledynamictick");

                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true))
                    {
                        key?.SetValue("Win32PrioritySeparation", 2, RegistryValueKind.DWord); // Default Windows priority
                    }

                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true))
                    {
                        key?.SetValue("LargeSystemCache", 0, RegistryValueKind.DWord);
                        key?.DeleteValue("IoPageLimit", false);
                    }
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl"))
                        {
                            if (key == null) return false;
                            var val = key.GetValue("Win32PrioritySeparation");
                            return val != null && Convert.ToInt32(val) == 38;
                        }
                    }
                    catch { return false; }
                }
            });

            // --- 6. Telemetry & Background Services ---
            Tweaks.Add(new Tweak
            {
                Id = "sys_telemetry",
                Name = "Windows Telemetri & Veri Toplama Kapatma",
                Category = "Sistem & Servisler",
                Description = "Windows arka plan telemetri servislerini (DiagTrack vb.) ve veri toplamayı kapatır. Boşta işlemci ve disk kullanımını düşürür.",
                ApplyAction = () =>
                {
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection"))
                    {
                        key.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                    }
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection"))
                    {
                        key.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                    }
                    // Disable DiagTrack service
                    RunCmd("sc config DiagTrack start= disabled");
                    RunCmd("sc stop DiagTrack");
                },
                RestoreAction = () =>
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", true))
                    {
                        key?.DeleteValue("AllowTelemetry", false);
                    }
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", true))
                    {
                        key?.DeleteValue("AllowTelemetry", false);
                    }
                    // Re-enable DiagTrack
                    RunCmd("sc config DiagTrack start= auto");
                    RunCmd("sc start DiagTrack");
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection"))
                        {
                            if (key == null) return false;
                            var val = key.GetValue("AllowTelemetry");
                            return val != null && Convert.ToInt32(val) == 0;
                        }
                    }
                    catch { return false; }
                }
            });

            // --- 7. Monitor Input Lag & D3D Low Latency Tweak (CS2/Valorant Special) ---
            Tweaks.Add(new Tweak
            {
                Id = "gpu_low_latency",
                Name = "CS2 / Valorant Monitör & Gecikme (ms) Düşürme",
                Category = "Ekran Kartı (GPU)",
                Description = "Ekran kartının kare kuyruğunu (Flip Queue Size / Pre-rendered frames) 1'e sabitleyerek CPU tamponlamasını kapatır. Monitör tepki süresini ve giriş gecikmesini (input lag) sıfıra indirir.",
                ApplyAction = () =>
                {
                    // Direct3D MaxPreRenderedFrames override (reduces D3D engine latency to absolute minimum)
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Direct3D"))
                    {
                        key.SetValue("MaxPreRenderedFrames", 1, RegistryValueKind.DWord);
                    }
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Direct3D"))
                    {
                        key.SetValue("MaxPreRenderedFrames", 1, RegistryValueKind.DWord);
                    }

                    // Global DWM & graphics low latency registry tweaks
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\cs2.exe"))
                    {
                        key.SetValue("DisableMaximizedWindowedInputMode", 1, RegistryValueKind.DWord); // Bypasses windowed input delay for CS2
                    }

                    // Optimize keyboard and mouse input response rate (reduces ms delay)
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Accessibility\Keyboard Response"))
                    {
                        key.SetValue("AutoRepeatDelay", "200", RegistryValueKind.String);
                        key.SetValue("AutoRepeatRate", "6", RegistryValueKind.String);
                        key.SetValue("DelayBeforeAcceptance", "0", RegistryValueKind.String);
                        key.SetValue("Flags", "59", RegistryValueKind.String);
                    }
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse"))
                    {
                        key.SetValue("MouseSpeed", "0", RegistryValueKind.String);
                        key.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
                        key.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
                    }

                    // Force Nvidia Low Latency Mode to Ultra using standard PowerMizer registries if possible
                    string nVidiaPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                    using (var rootKey = Registry.LocalMachine.OpenSubKey(nVidiaPath, true))
                    {
                        if (rootKey != null)
                        {
                            foreach (string subkeyName in rootKey.GetSubKeyNames())
                            {
                                if (subkeyName.StartsWith("000"))
                                {
                                    using (var subKey = rootKey.OpenSubKey(subkeyName, true))
                                    {
                                        if (subKey != null)
                                        {
                                            subKey.SetValue("MaxQueuedFrames", 1, RegistryValueKind.DWord);
                                            subKey.SetValue("FlipQueueSize", 1, RegistryValueKind.DWord);
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                RestoreAction = () =>
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Direct3D", true))
                    {
                        key?.DeleteValue("MaxPreRenderedFrames", false);
                    }
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Direct3D", true))
                    {
                        key?.DeleteValue("MaxPreRenderedFrames", false);
                    }
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\cs2.exe", true))
                    {
                        key?.DeleteValue("DisableMaximizedWindowedInputMode", false);
                    }

                    using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\Keyboard Response", true))
                    {
                        key?.SetValue("AutoRepeatDelay", "1000", RegistryValueKind.String);
                        key?.SetValue("AutoRepeatRate", "31", RegistryValueKind.String);
                        key?.SetValue("DelayBeforeAcceptance", "1000", RegistryValueKind.String);
                        key?.SetValue("Flags", "126", RegistryValueKind.String);
                    }
                    using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true))
                    {
                        key?.SetValue("MouseSpeed", "1", RegistryValueKind.String);
                        key?.SetValue("MouseThreshold1", "6", RegistryValueKind.String);
                        key?.SetValue("MouseThreshold2", "10", RegistryValueKind.String);
                    }
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Direct3D"))
                        {
                            if (key == null) return false;
                            var val = key.GetValue("MaxPreRenderedFrames");
                            return val != null && Convert.ToInt32(val) == 1;
                        }
                    }
                    catch { return false; }
                }
            });

            // --- 8. Disable CPU Power Throttling & C-States (Forces Max Wattage & FPS) ---
            Tweaks.Add(new Tweak
            {
                Id = "cpu_no_throttle",
                Name = "Gelişmiş Güç & Maksimum FPS (Throttling Kapatma)",
                Category = "CPU & Zamanlayıcı",
                Description = "İşlemcinin güç tasarrufu için watt tüketimini düşürmesini (C-States / CPU Throttling) tamamen kapatır. Sıcaklık ve güç kısıtlamalarını es geçerek her zaman maksimum saat hızında en yüksek FPS değerini almanızı sağlar.",
                ApplyAction = () =>
                {
                    // Disable Windows Power Throttling globally
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling"))
                    {
                        key.SetValue("PowerThrottlingOff", 1, RegistryValueKind.DWord);
                    }

                    // Disable CPU Core Parking & Idle scaling in the current Power Scheme
                    RunCmd("powercfg /setactive SCHEME_MIN"); // Force High Performance plan
                    RunCmd("powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES 100");
                    RunCmd("powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMAXCORES 100");
                    
                    // Disable Processor Idle States
                    RunCmd("powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 100");
                    RunCmd("powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100");
                    RunCmd("powercfg /active SCHEME_CURRENT");

                    // Force Nvidia GPU PowerMizer to Maximum Performance to bypass GPU power throttling
                    string nVidiaPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                    using (var rootKey = Registry.LocalMachine.OpenSubKey(nVidiaPath, true))
                    {
                        if (rootKey != null)
                        {
                            foreach (string subkeyName in rootKey.GetSubKeyNames())
                            {
                                if (subkeyName.StartsWith("000"))
                                {
                                    using (var subKey = rootKey.OpenSubKey(subkeyName, true))
                                    {
                                        if (subKey != null)
                                        {
                                            subKey.SetValue("PowerMizerEnable", 1, RegistryValueKind.DWord);
                                            subKey.SetValue("PowerMizerLevel", 1, RegistryValueKind.DWord); // Max Perf
                                            subKey.SetValue("PowerMizerLevelAC", 1, RegistryValueKind.DWord);
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                RestoreAction = () =>
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", true))
                    {
                        key?.DeleteValue("PowerThrottlingOff", false);
                    }

                    RunCmd("powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 5");
                    RunCmd("powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 100");
                    RunCmd("powercfg /active SCHEME_CURRENT");

                    string nVidiaPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                    using (var rootKey = Registry.LocalMachine.OpenSubKey(nVidiaPath, true))
                    {
                        if (rootKey != null)
                        {
                            foreach (string subkeyName in rootKey.GetSubKeyNames())
                            {
                                if (subkeyName.StartsWith("000"))
                                {
                                    using (var subKey = rootKey.OpenSubKey(subkeyName, true))
                                    {
                                        subKey?.DeleteValue("PowerMizerEnable", false);
                                        subKey?.DeleteValue("PowerMizerLevel", false);
                                        subKey?.DeleteValue("PowerMizerLevelAC", false);
                                    }
                                }
                            }
                        }
                    }
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling"))
                        {
                            if (key == null) return false;
                            var val = key.GetValue("PowerThrottlingOff");
                            return val != null && Convert.ToInt32(val) == 1;
                        }
                    }
                    catch { return false; }
                }
            });

            // --- 9. Advanced Gaming Engine & Shader Cache Tweaks (Eliminates FPS Stutter & Frame Drops) ---
            Tweaks.Add(new Tweak
            {
                Id = "advanced_gaming_tweaks",
                Name = "Gelişmiş Oyun Motoru & DirectX Gölgelendirici (Shader) Önbelleği",
                Category = "Ekran Kartı (GPU)",
                Description = "DirectX Shader Önbellek boyut sınırlamasını kaldırarak önbelleği 10 GB yapar (CS2, Valorant ve AAA oyunlardaki anlık donmaları tamamen çözer). AMD GPU'lar için ULPS derin uykusunu kapatarak kararlı saat hızları sağlar. Ağ kartları için donanımsal kesme yumuşatmasını optimize ederek gecikmeyi en aza indirir.",
                ApplyAction = () =>
                {
                    // 1. Maximize NVIDIA OpenGL/DirectX Shader Cache Size to 10GB (Eliminates compile-time frame drops)
                    try
                    {
                        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\NVIDIA Corporation\Global\OpenGL"))
                        {
                            key.SetValue("WriteRegistryForShaderCache", 1, RegistryValueKind.DWord);
                            key.SetValue("ShaderCacheMaxSizeBytes", 10737418240, RegistryValueKind.QWord); // 10 GB Cache
                        }
                    }
                    catch { }

                    // 2. Disable AMD ULPS (Ultra Low Power State) to prevent core clock drops and system stuttering on AMD cards
                    try
                    {
                        using (var rootKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", true))
                        {
                            if (rootKey != null)
                            {
                                foreach (string subkeyName in rootKey.GetSubKeyNames())
                                {
                                    if (subkeyName.StartsWith("000"))
                                    {
                                        using (var subKey = rootKey.OpenSubKey(subkeyName, true))
                                        {
                                            if (subKey != null && subKey.GetValue("EnableUlps") != null)
                                            {
                                                subKey.SetValue("EnableUlps", 0, RegistryValueKind.DWord);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    // 3. Optimize Network Interface Interrupt Moderation and Task Offload via Netsh to cut DPC latency
                    RunCmd("netsh int ip set global taskoffload=enabled");
                },
                RestoreAction = () =>
                {
                    // Restore default NVIDIA Shader Cache
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NVIDIA Corporation\Global\OpenGL", true))
                        {
                            key?.DeleteValue("WriteRegistryForShaderCache", false);
                            key?.DeleteValue("ShaderCacheMaxSizeBytes", false);
                        }
                    }
                    catch { }

                    // Restore default AMD ULPS
                    try
                    {
                        using (var rootKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}", true))
                        {
                            if (rootKey != null)
                            {
                                foreach (string subkeyName in rootKey.GetSubKeyNames())
                                {
                                    if (subkeyName.StartsWith("000"))
                                    {
                                        using (var subKey = rootKey.OpenSubKey(subkeyName, true))
                                        {
                                            if (subKey != null && subKey.GetValue("EnableUlps") != null)
                                            {
                                                subKey.SetValue("EnableUlps", 1, RegistryValueKind.DWord);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                },
                IsAppliedCheck = () =>
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NVIDIA Corporation\Global\OpenGL"))
                        {
                            if (key == null) return false;
                            var val = key.GetValue("ShaderCacheMaxSizeBytes");
                            return val != null && Convert.ToInt64(val) == 10737418240;
                        }
                    }
                    catch { return false; }
                }
            });
        }

        // --- Memory Purging (RAM Standby List Cleaner) ---
        public static long ClearStandbyList()
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                // Purge Windows Memory Standby List
                int command = 4; // MemoryPurgeStandbyList
                buffer = Marshal.AllocHGlobal(sizeof(int));
                Marshal.WriteInt32(buffer, command);
                uint result = NtSetSystemInformation(80, buffer, sizeof(int));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Standby List Cleaning Error: {ex.Message}");
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            // Run empty working set for idle/background processes to reclaim extra RAM
            long reclaimedBytes = EmptyBackgroundProcessesWorkingSet();
            return reclaimedBytes;
        }

        private static long EmptyBackgroundProcessesWorkingSet()
        {
            long freedMemory = 0;
            Process[] processes = Process.GetProcesses();
            foreach (Process proc in processes)
            {
                try
                {
                    // Skip essential processes and the current process
                    if (proc.Id == Process.GetCurrentProcess().Id || proc.Id == 0 || proc.Id == 4)
                        continue;

                    // Skip Vanguard, other anti-cheats, development tools, and main social/browser apps to prevent UI/chat stutters
                    string procNameLower = proc.ProcessName.ToLower();
                    if (procNameLower.Contains("vanguard") || 
                        procNameLower.Contains("vgc") || 
                        procNameLower.Contains("vgk") || 
                        procNameLower.Contains("easyanticheat") || 
                        procNameLower.Contains("battleye") ||
                        procNameLower.Contains("faceit") ||
                        procNameLower.Contains("antigravity") ||
                        procNameLower.Contains("gemini") ||
                        procNameLower.Contains("node") ||
                        procNameLower.Contains("powershell") ||
                        procNameLower.Contains("steam") ||
                        procNameLower.Contains("discord") ||
                        procNameLower.Contains("chrome") ||
                        procNameLower.Contains("msedge") ||
                        procNameLower.Contains("opera") ||
                        procNameLower.Contains("spotify"))
                    {
                        continue;
                    }

                    // Skip games or high priority processes if we can, but empty working set only works on user-mode processes we can open.
                    // We only target processes running in normal or lower priority to avoid touching active games.
                    if (proc.BasePriority > 8) 
                        continue; 

                    long workingSetBefore = proc.WorkingSet64;
                    IntPtr hProcess = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_INFORMATION, false, (uint)proc.Id);
                    if (hProcess != IntPtr.Zero)
                    {
                        EmptyWorkingSet(hProcess);
                        CloseHandle(hProcess);
                        proc.Refresh();
                        long workingSetAfter = proc.WorkingSet64;
                        if (workingSetBefore > workingSetAfter)
                        {
                            freedMemory += (workingSetBefore - workingSetAfter);
                        }
                    }
                }
                catch
                {
                    // Ignore processes we don't have access to (system services, protected processes)
                }
                finally
                {
                    proc.Dispose(); // Free process resources and handles immediately
                }
            }
            return freedMemory;
        }

        // --- System Restore Point Creation ---
        public static bool CreateRestorePoint()
        {
            try
            {
                string script = "Checkpoint-Computer -Description \"Antigravity FPS Optimizer Geri Yukleme\" -RestorePointType MODIFY_SETTINGS";
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    Verb = "runas",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process? proc = Process.Start(psi);
                proc?.WaitForExit();
                return proc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        // --- Temp Files Cleaner ---
        public static long CleanTempFiles()
        {
            long bytesCleaned = 0;
            string[] tempPaths = new string[]
            {
                Path.GetTempPath(), // User Temp
                Environment.ExpandEnvironmentVariables(@"%systemroot%\Temp"), // System Temp
                Environment.ExpandEnvironmentVariables(@"%systemroot%\Prefetch") // Prefetch
            };

            foreach (var path in tempPaths)
            {
                if (!Directory.Exists(path)) continue;

                var dirInfo = new DirectoryInfo(path);
                // Clean files
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    try
                    {
                        long size = file.Length;
                        file.Delete();
                        bytesCleaned += size;
                    }
                    catch { /* File is locked by active process, ignore */ }
                }
                // Clean directories
                foreach (var dir in dirInfo.EnumerateDirectories())
                {
                    try
                    {
                        long size = GetDirectorySize(dir);
                        dir.Delete(true);
                        bytesCleaned += size;
                    }
                    catch { /* Dir is locked, ignore */ }
                }
            }
            return bytesCleaned;
        }

        private static long GetDirectorySize(DirectoryInfo d)
        {
            long size = 0;
            try
            {
                // Add file sizes.
                FileInfo[] fis = d.GetFiles();
                foreach (FileInfo fi in fis)
                {
                    size += fi.Length;
                }
                // Add subdirectory sizes.
                DirectoryInfo[] dis = d.GetDirectories();
                foreach (DirectoryInfo di in dis)
                {
                    size += GetDirectorySize(di);
                }
            }
            catch { }
            return size;
        }

        // --- Helper CLI Executors ---
        private static void RunCmd(string command)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                Process.Start(psi)?.WaitForExit();
            }
            catch { }
        }

        private static string RunCmdOutput(string command)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {command}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string outStr = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();
                        return outStr;
                    }
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
