using System.IO;
using System.Text.Json;
using S7.Net;

namespace S7Explorer;

/// <summary>
/// EN: Class that manages PLC connection settings.
/// TR: PLC bağlantı ayarlarını yöneten sınıf.
/// </summary>
public class ConnectionSettings
{
    public string CpuType { get; set; } = "S7-1200";
    public string IpAddress { get; set; } = "192.168.0.1";
    public int Port { get; set; } = 102;
    public short Rack { get; set; } = 0;
    public short Slot { get; set; } = 1;
    public string Theme { get; set; } = "Light";
    public string Language { get; set; } = "en-US";

    private const string SettingsFileName = "settings.json";
    private static readonly string SettingsFilePath = 
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);

    /// <summary>
    /// EN: Loads settings from the JSON file.
    /// TR: Ayarları JSON dosyasından yükler.
    /// </summary>
    public static ConnectionSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var jsonContent = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<ConnectionSettings>(jsonContent);
                return settings ?? new ConnectionSettings();
            }
        }
        catch
        {
            // Yükleme hatası durumunda varsayılan ayarlar kullanılır
        }

        return new ConnectionSettings();
    }

    /// <summary>
    /// EN: Saves settings to the JSON file.
    /// TR: Ayarları JSON dosyasına kaydeder.
    /// </summary>
    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var jsonContent = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsFilePath, jsonContent);
        }
        catch (Exception ex)
        {
            throw new Exception(LocalizationManager.Instance.T("Ex_SettingsSaveFailed", ex.Message), ex);
        }
    }

    /// <summary>
    /// EN: Validates the settings.
    /// TR: Ayarları kontrol eder.
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(IpAddress) && 
               !string.IsNullOrWhiteSpace(CpuType) &&
               Port > 0 && Port <= 65535 &&
               Rack >= 0 && 
               Slot >= 0;
    }
}
