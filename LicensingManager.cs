using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32;

namespace AntigravityFPSOptimizer
{
    public static class LicensingManager
    {
        public enum ActivationResult
        {
            Success,
            AdminSuccess,
            Expired,
            InvalidKey,
            LockedToOtherHwid,
            DatabaseError,
            NoInternet
        }

        public class LicenseRecord
        {
            public string LicenseKey { get; set; } = string.Empty;
            public string? Hwid { get; set; }
            public int IsActivated { get; set; }
            public string? ActivationDate { get; set; }
            public string ExpirationDate { get; set; } = "unlimited";
            public int DurationDays { get; set; } = 99999;
            public string LicenseType { get; set; } = "Standard";
        }

        // --- WIN32 ANTI-DEBUGGING API IMPORTS ---
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] ref bool isPresent);

        // --- XOR OBFUSCATED STRINGS (Prevents Search-Based Decryption in dnSpy/ILSpy) ---
        // XOR Encryption Key
        private const byte ObfuscationKey = 0x5E;

        // "https://mustik-fps-licensing-default-rtdb.firebaseio.com/" encrypted with XOR 0x5E
        private static readonly byte[] EncryptedUrl = new byte[] { 
            0x36, 0x2A, 0x2A, 0x2E, 0x2D, 0x64, 0x71, 0x71, 0x33, 0x2B, 0x2D, 0x2A, 0x37, 0x35, 0x73, 0x38, 
            0x2E, 0x2D, 0x73, 0x32, 0x37, 0x3D, 0x3B, 0x30, 0x2D, 0x37, 0x30, 0x39, 0x73, 0x3A, 0x3B, 0x38, 
            0x3F, 0x2B, 0x32, 0x2A, 0x73, 0x2C, 0x2A, 0x3A, 0x3C, 0x70, 0x38, 0x37, 0x2C, 0x3B, 0x3C, 0x3F, 
            0x2D, 0x3B, 0x37, 0x31, 0x70, 0x3D, 0x31, 0x33, 0x71 
        };

        // One-way cryptographically secure SHA-256 hash of the Admin Master Key
        private const string AdminMasterKeyHash = "389d0adf2799804616d369f77afeb0c6433fdeaa01e5a5ce53bd9d6c4f9c525c";

        // "activation.lic" encrypted with XOR 0x5E
        private static readonly byte[] EncryptedLicenseCache = new byte[] { 
            0x3F, 0x3D, 0x2A, 0x37, 0x28, 0x3F, 0x2A, 0x37, 0x31, 0x30, 0x70, 0x32, 0x37, 0x3D 
        };

        // "licenses" encrypted with XOR 0x5E
        private static readonly byte[] EncryptedLicensesPath = new byte[] { 
            0x32, 0x37, 0x3D, 0x3B, 0x30, 0x2D, 0x3B, 0x2D 
        };

        // Registry Paths & Values encrypted with XOR 0x5E
        private static readonly byte[] EncryptedBiosPath = new byte[] { 
            0x16, 0x1F, 0x0C, 0x1A, 0x09, 0x1F, 0x0C, 0x1B, 0x02, 0x1A, 0x1B, 0x0D, 0x1D, 0x0C, 0x17, 0x0E, 
            0x0A, 0x17, 0x11, 0x10, 0x02, 0x0D, 0x27, 0x2D, 0x2A, 0x3B, 0x33, 0x02, 0x1C, 0x17, 0x11, 0x0D 
        }; // "HARDWARE\DESCRIPTION\System\BIOS"
        
        private static readonly byte[] EncryptedCryptoPath = new byte[] { 
            0x0D, 0x11, 0x18, 0x0A, 0x09, 0x1F, 0x0C, 0x1B, 0x02, 0x13, 0x37, 0x3D, 0x2C, 0x31, 0x2D, 0x31, 
            0x38, 0x2A, 0x02, 0x1D, 0x2C, 0x27, 0x2E, 0x2A, 0x31, 0x39, 0x2C, 0x3F, 0x2E, 0x36, 0x27 
        }; // "SOFTWARE\Microsoft\Cryptography"

        private static readonly byte[] EncryptedBoardSerial = new byte[] { 
            0x1C, 0x3F, 0x2D, 0x3B, 0x1C, 0x31, 0x3F, 0x2C, 0x3A, 0x0D, 0x3B, 0x2C, 0x37, 0x3F, 0x32, 0x10, 
            0x2B, 0x33, 0x3C, 0x3B, 0x2C 
        }; // "BaseBoardSerialNumber"

        private static readonly byte[] EncryptedSystemSerial = new byte[] { 
            0x0D, 0x27, 0x2D, 0x2A, 0x3B, 0x33, 0x0D, 0x3B, 0x2C, 0x37, 0x3F, 0x32, 0x10, 0x2B, 0x33, 0x3C, 
            0x3B, 0x2C 
        }; // "SystemSerialNumber"

        private static readonly byte[] EncryptedBoardProduct = new byte[] { 
            0x1C, 0x3F, 0x2D, 0x3B, 0x1C, 0x31, 0x3F, 0x2C, 0x3A, 0x0E, 0x2C, 0x31, 0x3A, 0x2B, 0x3D, 0x2A 
        }; // "BaseBoardProduct"

        private static readonly byte[] EncryptedMachineGuid = new byte[] { 
            0x13, 0x3F, 0x3D, 0x36, 0x37, 0x30, 0x3B, 0x19, 0x2B, 0x37, 0x3A 
        }; // "MachineGuid"

        private static readonly byte[] EncryptedManufacturer = new byte[] { 
            0x0D, 0x27, 0x2D, 0x2A, 0x3B, 0x33, 0x13, 0x3F, 0x30, 0x2B, 0x38, 0x3F, 0x3D, 0x2A, 0x2B, 0x2C, 
            0x3B, 0x2C 
        }; // "SystemManufacturer"

        private static readonly byte[] EncryptedProductName = new byte[] { 
            0x0D, 0x27, 0x2D, 0x2A, 0x3B, 0x33, 0x0E, 0x2C, 0x31, 0x3A, 0x2B, 0x3D, 0x2A, 0x10, 0x3F, 0x33, 
            0x3B 
        }; // "SystemProductName"

        // Decryption method executed on-the-fly (Never exposes values in static heap!)
        private static string D(byte[] data)
        {
            byte[] decrypted = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                decrypted[i] = (byte)(data[i] ^ ObfuscationKey);
            }
            return Encoding.UTF8.GetString(decrypted);
        }

        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "AntigravityFPSOptimizer"
        );
        
        private static string LicenseCachePath => Path.Combine(FolderPath, D(EncryptedLicenseCache));

        // Checks if a given key is the Admin Master Key using SHA-256 hash comparison
        public static bool IsAdminKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            try
            {
                using (var sha = SHA256.Create())
                {
                    byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key.Trim().ToUpperInvariant()));
                    var sb = new StringBuilder();
                    foreach (byte b in bytes)
                    {
                        sb.Append(b.ToString("x2"));
                    }
                    return sb.ToString() == AdminMasterKeyHash;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void DeleteCachedLicense()
        {
            try
            {
                if (File.Exists(LicenseCachePath))
                {
                    File.Delete(LicenseCachePath);
                }
            }
            catch { }
        }

        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        private static string? _cachedHwid;

        static LicensingManager()
        {
            // Force TLS 1.2 and TLS 1.3 to avoid handshake failures on custom Windows setups
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= 
                    System.Net.SecurityProtocolType.Tls12 | 
                    System.Net.SecurityProtocolType.Tls13;
            }
            catch { }

            // Execute strong anti-reverse-engineering security checks immediately on static instantiation
            PerformSecurityChecks();

            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }
        }

        // --- WORLD-CLASS ANTI-DEBUGGING AND ANTI-VM SUITE (CRITICAL PROTECTION) ---
        public static void PerformSecurityChecks()
        {
            // 1. Check local debugger attachment via .NET framework
            if (Debugger.IsAttached)
            {
                KillSelf();
            }

            // 2. Direct Win32 API Check for local debuggers (dnSpy, x64dbg, Cheat Engine)
            try
            {
                if (IsDebuggerPresent())
                {
                    KillSelf();
                }
            }
            catch { }

            // 3. Direct Win32 API Check for remote/attached ring-3 system debuggers
            try
            {
                bool isDebuggerAttached = false;
                CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref isDebuggerAttached);
                if (isDebuggerAttached)
                {
                    KillSelf();
                }
            }
            catch { }

            // 4. Anti-Virtual Machine & Sandbox Detection (Bypasses cracker tracking environments)
            try
            {
                using (var biosKey = Registry.LocalMachine.OpenSubKey(D(EncryptedBiosPath)))
                {
                    if (biosKey != null)
                    {
                        string manufacturer = biosKey.GetValue(D(EncryptedManufacturer))?.ToString()?.ToLower() ?? "";
                        string model = biosKey.GetValue(D(EncryptedProductName))?.ToString()?.ToLower() ?? "";
                        
                        if (manufacturer.Contains("vmware") || manufacturer.Contains("virtualbox") || 
                            manufacturer.Contains("qemu") || manufacturer.Contains("xen") ||
                            manufacturer.Contains("hyper-v") || manufacturer.Contains("parallels") ||
                            model.Contains("vmware") || model.Contains("virtual") || model.Contains("hyper-v") ||
                            model.Contains("sandbox"))
                        {
                            KillSelf();
                        }
                    }
                }
            }
            catch { }
        }

        private static void KillSelf()
        {
            // Destroys licensing caches and immediately terminates to prevent cracking analysis
            try
            {
                if (File.Exists(LicenseCachePath))
                {
                    File.Delete(LicenseCachePath);
                }
            }
            catch { }

            Process.GetCurrentProcess().Kill();
            Environment.Exit(0);
        }

        // Generates a unique HWID strictly locked to the motherboard serial and Machine Guid
        public static string GetHardwareId()
        {
            PerformSecurityChecks(); // Force safety check

            if (!string.IsNullOrEmpty(_cachedHwid))
                return _cachedHwid;

            try
            {
                var sb = new StringBuilder();

                // 1. Get Motherboard Serial Number from Registry
                using (var biosKey = Registry.LocalMachine.OpenSubKey(D(EncryptedBiosPath)))
                {
                    if (biosKey != null)
                    {
                        var motherboardSerial = biosKey.GetValue(D(EncryptedBoardSerial)) ?? 
                                                 biosKey.GetValue(D(EncryptedSystemSerial));
                        if (motherboardSerial != null)
                        {
                            sb.Append(motherboardSerial.ToString()?.Trim());
                        }

                        var boardProduct = biosKey.GetValue(D(EncryptedBoardProduct));
                        if (boardProduct != null)
                        {
                            sb.Append(boardProduct.ToString()?.Trim());
                        }
                    }
                }

                // 2. Get Windows Cryptography MachineGuid
                using (var cryptoKey = Registry.LocalMachine.OpenSubKey(D(EncryptedCryptoPath)))
                {
                    if (cryptoKey != null)
                    {
                        var machineGuid = cryptoKey.GetValue(D(EncryptedMachineGuid));
                        if (machineGuid != null)
                        {
                            sb.Append(machineGuid.ToString()?.Trim());
                        }
                    }
                }

                // 3. Fallback
                if (sb.Length == 0)
                {
                    sb.Append(Environment.MachineName);
                    sb.Append(Environment.ProcessorCount);
                    sb.Append(Environment.UserName);
                }

                // 4. Hash the combined hardware info
                using (var sha = SHA256.Create())
                {
                    byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                    var hashSb = new StringBuilder();
                    foreach (byte b in bytes)
                    {
                        hashSb.Append(b.ToString("X2"));
                    }
                    _cachedHwid = hashSb.ToString().Substring(0, 32);
                }
            }
            catch
            {
                _cachedHwid = "MUSTIK-DEV-FALLBACK-HWID-00000000";
            }

            return _cachedHwid;
        }

        // Checks if this computer (motherboard) is already activated in the online database and is not expired
        public static bool CheckActivationState()
        {
            PerformSecurityChecks(); // Force safety check

            try
            {
                if (!File.Exists(LicenseCachePath))
                    return false;

                string cachedKey = File.ReadAllText(LicenseCachePath).Trim();
                if (string.IsNullOrEmpty(cachedKey)) return false;

                // Admin Master Key always bypasses client activation check, allowing normal execution
                if (IsAdminKey(cachedKey))
                    return true;

                var checkTask = Task.Run(() => CheckOnlineActivation(cachedKey));
                checkTask.Wait(4000); // 4 seconds timeout

                return checkTask.IsCompleted && checkTask.Result;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> CheckOnlineActivation(string key)
        {
            try
            {
                string hwid = GetHardwareId();
                string checkUrl = $"{D(EncryptedUrl)}{D(EncryptedLicensesPath)}/{key}.json";
                var response = await Client.GetAsync(checkUrl);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content) && content.Trim() != "null")
                    {
                        var record = JsonSerializer.Deserialize<LicenseRecord>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (record != null && record.Hwid == hwid && record.IsActivated == 1)
                        {
                            // Check Expiration
                            if (record.ExpirationDate != "unlimited" && !string.IsNullOrEmpty(record.ExpirationDate))
                            {
                                if (DateTime.TryParse(record.ExpirationDate, out DateTime expDate))
                                {
                                    if (DateTime.Now > expDate)
                                    {
                                        return false; // License is expired!
                                    }
                                }
                            }
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        // Tries to activate a license key on the current computer (motherboard) against the online database
        public static ActivationResult TryActivate(string key)
        {
            PerformSecurityChecks(); // Force safety check

            if (string.IsNullOrWhiteSpace(key))
                return ActivationResult.InvalidKey;

            key = key.Trim();

            // Admin Master Key activation check
            if (IsAdminKey(key))
            {
                File.WriteAllText(LicenseCachePath, key);
                return ActivationResult.AdminSuccess;
            }

            try
            {
                var task = Task.Run(() => TryActivateOnline(key));
                task.Wait(7000); // 7 seconds timeout

                if (task.IsCompleted)
                    return task.Result;
                else
                    return ActivationResult.NoInternet;
            }
            catch (AggregateException ae)
            {
                if (ae.InnerException is HttpRequestException)
                    return ActivationResult.NoInternet;
                return ActivationResult.DatabaseError;
            }
            catch
            {
                return ActivationResult.DatabaseError;
            }
        }

        private static async Task<ActivationResult> TryActivateOnline(string key)
        {
            try
            {
                string hwid = GetHardwareId();
                string queryUrl = $"{D(EncryptedUrl)}{D(EncryptedLicensesPath)}/{key}.json";
                
                var response = await Client.GetAsync(queryUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return ActivationResult.DatabaseError;
                }

                string content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(content) || content.Trim() == "null")
                {
                    return ActivationResult.InvalidKey;
                }

                var record = JsonSerializer.Deserialize<LicenseRecord>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (record == null)
                {
                    return ActivationResult.InvalidKey;
                }

                if (record.IsActivated == 1)
                {
                    if (record.Hwid == hwid)
                    {
                        // Check Expiration
                        if (record.ExpirationDate != "unlimited" && !string.IsNullOrEmpty(record.ExpirationDate))
                        {
                            if (DateTime.TryParse(record.ExpirationDate, out DateTime expDate))
                            {
                                if (DateTime.Now > expDate)
                                {
                                    return ActivationResult.Expired;
                                }
                            }
                        }
                        
                        File.WriteAllText(LicenseCachePath, key);
                        return ActivationResult.Success;
                    }
                    else
                    {
                        return ActivationResult.LockedToOtherHwid;
                    }
                }

                // Activate key
                record.Hwid = hwid;
                record.IsActivated = 1;
                record.ActivationDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                if (record.DurationDays == 99999)
                {
                    record.ExpirationDate = "unlimited";
                }
                else
                {
                    record.ExpirationDate = DateTime.Now.AddDays(record.DurationDays).ToString("yyyy-MM-dd HH:mm:ss");
                }

                string updateJson = JsonSerializer.Serialize(record);
                var httpContent = new StringContent(updateJson, Encoding.UTF8, "application/json");
                
                var patchResponse = await Client.PutAsync(queryUrl, httpContent);
                if (patchResponse.IsSuccessStatusCode)
                {
                    File.WriteAllText(LicenseCachePath, key);
                    return ActivationResult.Success;
                }
                else
                {
                    return ActivationResult.DatabaseError;
                }
            }
            catch (HttpRequestException)
            {
                return ActivationResult.NoInternet;
            }
            catch
            {
                return ActivationResult.DatabaseError;
            }
        }

        // Generates a new random license key with specified duration and uploads it to the Firebase cloud database
        public static string GenerateNewKey(int durationDays)
        {
            PerformSecurityChecks(); // Force safety check

            try
            {
                var task = Task.Run(() => GenerateNewKeyOnline(durationDays));
                task.Wait(12000);
                return task.Result;
            }
            catch (AggregateException ae)
            {
                var inner = ae.Flatten().InnerException;
                return $"HATA: Sunucuya bağlanılamadı! (Aggregate: {inner?.Message} - {inner?.InnerException?.Message})";
            }
            catch (Exception ex)
            {
                return $"HATA: Sunucuya bağlanılamadı! (Exception: {ex.Message})";
            }
        }

        private static async Task<string> GenerateNewKeyOnline(int durationDays)
        {
            try
            {
                string suffix;
                using (var rng = RandomNumberGenerator.Create())
                {
                    byte[] bytes = new byte[8];
                    rng.GetBytes(bytes);
                    suffix = Convert.ToHexString(bytes).Substring(0, 12);
                }

                string typePrefix = durationDays == 1 ? "MDEV-1DAY" : 
                                    durationDays == 30 ? "MDEV-30DAY" : "MDEV-UNLIM";

                string generatedKey = $"{typePrefix}-{suffix.Substring(0, 4)}-{suffix.Substring(4, 4)}-{suffix.Substring(8, 4)}".ToUpper();
                string licenseType = durationDays == 1 ? "1-Day Temporary" :
                                     durationDays == 30 ? "30-Day Temporary" : "VIP-Sınırsız";

                var newLicense = new LicenseRecord
                {
                    LicenseKey = generatedKey,
                    IsActivated = 0,
                    DurationDays = durationDays,
                    LicenseType = licenseType,
                    ExpirationDate = durationDays == 99999 ? "unlimited" : ""
                };

                string queryUrl = $"{D(EncryptedUrl)}{D(EncryptedLicensesPath)}/{generatedKey}.json";
                string json = JsonSerializer.Serialize(newLicense);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await Client.PutAsync(queryUrl, httpContent);
                if (response.IsSuccessStatusCode)
                {
                    return generatedKey;
                }
                else
                {
                    return "HATA: Veritabanı yazma hatası!";
                }
            }
            catch (Exception ex)
            {
                return $"HATA: İnternet veya sunucu hatası! ({ex.Message} - {ex.InnerException?.Message})";
            }
        }

        // Returns active key if already activated on this computer
        public static string GetActiveKey()
        {
            try
            {
                if (File.Exists(LicenseCachePath))
                {
                    return File.ReadAllText(LicenseCachePath).Trim();
                }
            }
            catch { }
            return "Deneme Sürümü / Aktif Değil";
        }
    }
}
