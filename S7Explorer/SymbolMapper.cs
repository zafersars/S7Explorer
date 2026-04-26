using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace S7Explorer;

/// <summary>
/// EN: Symbol information (address, data type, default value, and description).
/// TR: Sembol bilgisi (adres, veri tipi, varsayılan değer ve açıklama).
/// </summary>
public class SymbolInfo
{
    public string PhysicalAddress { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty; // "BOOL", "BYTE", "WORD", "DWORD", "INT", "DINT", "REAL", vb.
    public string DefaultValue { get; set; } = string.Empty; // Varsayılan değer
    public string Description { get; set; } = string.Empty; // Açıklama/yorum
}

/// <summary>
/// EN: Converts symbolic addresses to physical PLC addresses.
/// TR: Sembolik adresleri fiziksel PLC adreslerine dönüştürür.
/// </summary>
public class SymbolMapper
{
    private readonly Dictionary<string, SymbolInfo> _symbolMap;
    private const string DefaultSymbolFileName = "symbols.json";
    private readonly string _symbolFilePath;

    public SymbolMapper()
    {
        _symbolMap = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);

        // Sembol dosyasının yolu (uygulama dizininde)
        _symbolFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultSymbolFileName);

        // Eğer daha önce kaydedilmiş JSON dosyası varsa yükle
        if (File.Exists(_symbolFilePath))
        {
            try
            {
                LoadFromJson(_symbolFilePath);
            }
            catch
            {
                // Yükleme hatası durumunda boş başla
            }
        }
        // Dosya yoksa boş başla (örnek veriler yüklenmez)
    }

    /// <summary>
    /// EN: Adds a new symbol mapping.
    /// TR: Yeni bir sembol eşlemesi ekler.
    /// </summary>
    public void AddSymbol(string symbolicName, string physicalAddress, string dataType, string defaultValue, string description)
    {
        if (string.IsNullOrWhiteSpace(symbolicName) || string.IsNullOrWhiteSpace(physicalAddress))
            throw new ArgumentException(LocalizationManager.Instance.T("Ex_SymbolNameOrAddressEmpty"));

        _symbolMap[symbolicName] = new SymbolInfo 
        { 
            PhysicalAddress = physicalAddress,
            DataType = dataType ?? string.Empty,
            DefaultValue = defaultValue ?? string.Empty,
            Description = description ?? string.Empty
        };
    }

    /// <summary>
    /// EN: Adds multiple symbol mappings.
    /// TR: Birden fazla sembol eşlemesi ekler.
    /// </summary>
    public void AddSymbols(Dictionary<string, SymbolInfo> symbols)
    {
        foreach (var symbol in symbols)
        {
            _symbolMap[symbol.Key] = symbol.Value;
        }
    }

    /// <summary>
    /// EN: Resolves a symbolic address to a physical address. Returns as-is if already physical.
    /// TR: Sembolik adresi fiziksel adrese dönüştürür. Zaten fiziksel ise olduğu gibi döner.
    /// </summary>
    public string Resolve(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException(LocalizationManager.Instance.T("Ex_AddressEmpty"), nameof(address));

        // Eğer sembol haritasında varsa, fiziksel adrese dönüştür
        if (_symbolMap.TryGetValue(address, out var symbolInfo))
        {
            return symbolInfo.PhysicalAddress;
        }

        // Yoksa, zaten fiziksel adres olduğunu varsay
        return address;
    }

    /// <summary>
    /// EN: Returns symbol information (address and type) for a symbolic address.
    /// TR: Sembolik adresten sembol bilgilerini (adres ve tip) döner.
    /// </summary>
    public SymbolInfo? GetSymbolInfo(string symbolicName)
    {
        if (_symbolMap.TryGetValue(symbolicName, out var symbolInfo))
        {
            return symbolInfo;
        }
        return null;
    }

    /// <summary>
    /// EN: Lists all symbols.
    /// TR: Tüm sembolleri listeler.
    /// </summary>
    public IReadOnlyDictionary<string, SymbolInfo> GetAllSymbols()
    {
        return _symbolMap;
    }

    /// <summary>
    /// EN: Loads the symbol table from a JSON file.
    /// TR: JSON dosyasından sembol tablosu yükler.
    /// </summary>
    public void LoadFromJson(string jsonFilePath)
    {
        if (!File.Exists(jsonFilePath))
            throw new FileNotFoundException(LocalizationManager.Instance.T("Ex_JsonFileNotFound"), jsonFilePath);

        var jsonContent = File.ReadAllText(jsonFilePath);

        // Önce yeni formatı dene (SymbolInfo ile)
        try
        {
            var symbolsWithType = JsonSerializer.Deserialize<Dictionary<string, SymbolInfo>>(jsonContent);
            if (symbolsWithType != null)
            {
                _symbolMap.Clear();
                foreach (var symbol in symbolsWithType)
                {
                    if (!string.IsNullOrWhiteSpace(symbol.Key) && 
                        !string.IsNullOrWhiteSpace(symbol.Value.PhysicalAddress))
                    {
                        _symbolMap[symbol.Key] = symbol.Value;
                    }
                }
                return;
            }
        }
        catch
        {
            // Yeni format başarısız, eski formatı dene
        }

        // Eski format (geriye dönük uyumluluk için)
        var symbols = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);
        if (symbols != null)
        {
            _symbolMap.Clear();
            foreach (var symbol in symbols)
            {
                if (!string.IsNullOrWhiteSpace(symbol.Key) && 
                    !string.IsNullOrWhiteSpace(symbol.Value))
                {
                    _symbolMap[symbol.Key] = new SymbolInfo
                    {
                        PhysicalAddress = symbol.Value,
                        DataType = string.Empty,
                        DefaultValue = string.Empty,
                        Description = string.Empty
                    };
                }
            }
        }
    }

    /// <summary>
    /// EN: Saves symbols to a JSON file.
    /// TR: Sembolleri JSON dosyasına kaydeder.
    /// </summary>
    public void SaveToJson(string jsonFilePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var jsonContent = JsonSerializer.Serialize(_symbolMap, options);
        File.WriteAllText(jsonFilePath, jsonContent);
    }

    /// <summary>
    /// EN: Saves symbols to the default JSON file (symbols.json).
    /// TR: Sembolleri varsayılan JSON dosyasına kaydeder (symbols.json).
    /// </summary>
    public void Save()
    {
        SaveToJson(_symbolFilePath);
    }



    /// <summary>
    /// EN: Clears all symbols.
    /// TR: Tüm sembolleri temizler.
    /// </summary>
    public void Clear()
    {
        _symbolMap.Clear();
    }
}
