using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace S7Explorer;

/// <summary>
/// EN: Parses Siemens S7 DB files and converts them to JSON format.
/// TR: Siemens S7 DB dosyalarını parse eder ve JSON formatına dönüştürür.
/// </summary>
public class DbParser
{
    private int _currentBitOffset;
    private int _currentByteOffset;
    private string _currentDbName = "";

    public DbParseResult Parse(string dbFilePath)
    {
        if (!File.Exists(dbFilePath))
            throw new FileNotFoundException(LocalizationManager.Instance.T("Ex_DbFileNotFound"), dbFilePath);

        var content = File.ReadAllText(dbFilePath, Encoding.UTF8);
        return ParseContent(content);
    }

    public DbParseResult ParseContent(string content)
    {
        var result = new DbParseResult();
        _currentBitOffset = 0;
        _currentByteOffset = 0;

        var lines = content.Split('\n').Select(l => l.Trim()).ToArray();

        // DB adını bul
        foreach (var line in lines)
        {
            if (line.StartsWith("DATA_BLOCK"))
            {
                var match = Regex.Match(line, @"DATA_BLOCK\s+""([^""]+)""");
                if (match.Success)
                {
                    _currentDbName = match.Groups[1].Value;
                    result.DbName = _currentDbName;
                }
                break;
            }
        }

        // Struct içeriğini parse et
        var structContent = ExtractStructContent(content);
        result.Structure = ParseStruct(structContent, 0);

        // BEGIN...END_DATA_BLOCK arasındaki gerçek varsayılan değerleri parse et
        result.DefaultValues = ParseDefaultValues(content);

        return result;
    }

    private string ExtractStructContent(string content)
    {
        var lines = content.Split('\n');
        var depth = 0;
        var sb = new StringBuilder();
        var started = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // İlk STRUCT satırını bul
            if (!started && line.Contains("STRUCT") && !line.Contains("END_STRUCT"))
            {
                started = true;
                depth = 1;
                continue; // "STRUCT" satırını atla
            }

            if (!started)
                continue;

            // Nested struct kontrolü
            if (Regex.IsMatch(line, @":\s*Struct", RegexOptions.IgnoreCase))
            {
                depth++;
            }

            // END_STRUCT kontrolü
            if (line.Contains("END_STRUCT"))
            {
                depth--;

                if (depth == 0)
                {
                    break;
                }
            }

            sb.AppendLine(lines[i]); // Orijinal satırı ekle (trim edilmemiş)
        }

        return sb.ToString();
    }

    private DbStructure ParseStruct(string content, int level)
    {
        var structure = new DbStructure { Level = level };
        var lines = content.Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("//") && !l.StartsWith("{"))
            .ToList();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            // END_STRUCT satırını atla
            if (line.Contains("END_STRUCT"))
            {
                continue;
            }

            // Field tanımı - çeşitli formatları destekle
            // Örnek: Life : Bool;   // Yorum
            // Örnek: "Time" : Struct
            // Örnek: Reserve_16 : Bool;
            // Örnek: "Result Length" : UInt;  // Boşluk içeren isim (tırnak içinde)
            // Örnek: Result : String[32];  // Boyutlu string
            var fieldMatch = Regex.Match(line, @"^\s*(?:""([^""]+)""|([^\s:]+))\s*(?:\{[^}]*\})?\s*:\s*(\w+)(?:\[(\d+)\])?\s*;?\s*(?://\s*(.*))?", RegexOptions.IgnoreCase);

            if (fieldMatch.Success)
            {
                // Field name ya grup 1'de (tırnaklar içinde) ya da grup 2'de (tırnaksız)
                var fieldName = !string.IsNullOrEmpty(fieldMatch.Groups[1].Value) 
                    ? fieldMatch.Groups[1].Value.Trim() 
                    : fieldMatch.Groups[2].Value.Trim();
                var fieldType = fieldMatch.Groups[3].Value;
                var arraySizeStr = fieldMatch.Groups[4].Value; // String boyutu (varsa)
                var comment = fieldMatch.Groups[5].Success ? fieldMatch.Groups[5].Value.Trim() : "";

                // String boyutunu parse et
                int stringLength = 254; // Varsayılan STRING/WSTRING uzunluğu
                if (!string.IsNullOrEmpty(arraySizeStr) &&
                    (fieldType.Equals("String", StringComparison.OrdinalIgnoreCase) ||
                     fieldType.Equals("WString", StringComparison.OrdinalIgnoreCase)))
                {
                    if (int.TryParse(arraySizeStr, out var parsedLength))
                    {
                        stringLength = parsedLength;
                    }
                }

                        // Struct ise, içeriğini parse et
                if (fieldType.Equals("Struct", StringComparison.OrdinalIgnoreCase))
                {
                    var nestedContent = ExtractNestedStruct(lines, ref i);
                    var startOffset = _currentByteOffset;
                    var startBitOffset = _currentBitOffset;

                    var nestedStruct = ParseStruct(nestedContent, level + 1);

                    structure.Fields.Add(new DbField
                    {
                        Name = fieldName,
                        Type = "Struct",
                        Comment = comment,
                        NestedStructure = nestedStruct,
                            ByteOffset = startOffset,
                            BitOffset = startBitOffset
                        });

                        // Nested struct parse edildikten sonra offset'ler zaten güncellenmiş durumda
                    // Eğer son bit offset 0'dan farklıysa, bir sonraki byte'a geç
                    if (_currentBitOffset > 0)
                    {
                        _currentByteOffset++;
                        _currentBitOffset = 0;
                    }

                    // Struct sonrası WORD alignment: Struct'lar 2'nin katı byte adresinde başlamalı
                    if (_currentByteOffset % 2 != 0)
                    {
                        _currentByteOffset++;
                    }
                }
                else
                {
                    // Alignment uygula (field oluşturmadan ÖNCE)
                    ApplyTypeAlignment(fieldType);

                    // Basit tip
                    var field = new DbField
                    {
                        Name = fieldName,
                        Type = fieldType,
                        Comment = comment,
                        ByteOffset = _currentByteOffset,
                        BitOffset = _currentBitOffset,
                        StringLength = stringLength // STRING boyutunu sakla
                    };

                    structure.Fields.Add(field);

                    // Boyutu ekle (field oluşturduktan SONRA)
                    // String/WString için boyut bilgisini gönder
                    if (fieldType.StartsWith("String", StringComparison.OrdinalIgnoreCase) ||
                        fieldType.StartsWith("WString", StringComparison.OrdinalIgnoreCase))
                    {
                        AddTypeSize(fieldType, stringLength);
                    }
                    else
                    {
                        AddTypeSize(fieldType);
                    }
                }
            }
        }

        return structure;
    }

    private string ExtractNestedStruct(List<string> lines, ref int currentIndex)
    {
        var sb = new StringBuilder();
        var depth = 0;

        currentIndex++; // "Struct" satırını atla

        while (currentIndex < lines.Count)
        {
            var line = lines[currentIndex];

            // Yeni nested struct başlangıcı - ÖNCE kontrol et
            if (Regex.IsMatch(line, @":\s*Struct", RegexOptions.IgnoreCase))
            {
                depth++;
            }

            // END_STRUCT kontrolü
            if (line.Contains("END_STRUCT"))
            {
                if (depth == 0)
                {
                    // Bu bizim struct'ımızın sonu
                    break;
                }

                depth--;
                sb.AppendLine(line);
                currentIndex++;
                continue;
            }

            sb.AppendLine(line);
            currentIndex++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// EN: Applies required byte alignment for the type (must be called BEFORE creating the field).
    /// TR: Tip için gerekli byte alignment'ı uygular (field oluşturmadan ÖNCE çağrılır).
    /// </summary>
    private void ApplyTypeAlignment(string type)
    {
        var upperType = type.ToUpperInvariant();

        // STRING[xx] formatını kontrol et
        if (upperType.StartsWith("STRING"))
        {
            // Bit offset varsa byte'ı tamamla
            if (_currentBitOffset > 0)
            {
                _currentByteOffset++;
                _currentBitOffset = 0;
            }
            // STRING alignment: 2'nin katı (WORD aligned)
            if (_currentByteOffset % 2 != 0)
            {
                _currentByteOffset++;
            }
            return;
        }

        switch (upperType)
        {
            case "BOOL":
                // Bool için alignment yok, bit seviyesinde çalışır
                break;

            case "BYTE":
            case "CHAR":
            case "SINT":
            case "USINT":
                // 1 byte: bit offset varsa tamamla
                if (_currentBitOffset > 0)
                {
                    _currentByteOffset++;
                    _currentBitOffset = 0;
                }
                break;

            case "WORD":
            case "INT":
            case "UINT":
            case "WCHAR":
            case "DATE":
            case "S5TIME":
                // 2 byte: WORD alignment
                if (_currentBitOffset > 0)
                {
                    _currentByteOffset++;
                    _currentBitOffset = 0;
                }
                if (_currentByteOffset % 2 != 0)
                {
                    _currentByteOffset++;
                }
                break;

            case "DWORD":
            case "DINT":
            case "UDINT":
            case "REAL":
            case "TIME":
            case "TIME_OF_DAY":
            case "TOD":
            case "DATE_AND_TIME":
            case "DT":
            case "LREAL":
            case "LINT":
            case "ULINT":
            case "LWORD":
            case "DTL":
                // S7 non-optimized DB: tüm çok-byte tipler için WORD (2-byte) alignment
                if (_currentBitOffset > 0)
                {
                    _currentByteOffset++;
                    _currentBitOffset = 0;
                }
                if (_currentByteOffset % 2 != 0)
                {
                    _currentByteOffset++;
                }
                break;

            default:
                // Bilinmeyen tipler için byte alignment
                if (_currentBitOffset > 0)
                {
                    _currentByteOffset++;
                    _currentBitOffset = 0;
                }
                break;
        }
    }

    /// <summary>
    /// Tip boyutunu offset'e ekler (field oluşturduktan SONRA çağrılır)
    /// </summary>
    private void AddTypeSize(string type, int stringLength = 254)
    {
        var upperType = type.ToUpperInvariant();

        // STRING[xx] formatını kontrol et
        if (upperType.StartsWith("STRING"))
        {
            // STRING yapısı: 2 byte (max + current length) + N byte (karakterler)
            _currentByteOffset += 2 + stringLength;
            return;
        }

        // WSTRING[xx] formatını kontrol et
        if (upperType.StartsWith("WSTRING"))
        {
            // WSTRING yapısı: 4 byte (max + current length ushort'lar) + N*2 byte (unicode karakterler)
            _currentByteOffset += 4 + stringLength * 2;
            return;
        }

        switch (upperType)
        {
            case "BOOL":
                _currentBitOffset++;
                if (_currentBitOffset >= 8)
                {
                    _currentByteOffset++;
                    _currentBitOffset = 0;
                }
                break;

            case "BYTE":
            case "CHAR":
            case "SINT":
            case "USINT":
                _currentByteOffset++;
                break;

            case "WORD":
            case "INT":
            case "UINT":
            case "WCHAR":
            case "DATE":
            case "S5TIME":
                _currentByteOffset += 2;
                break;

            case "DWORD":
            case "DINT":
            case "UDINT":
            case "REAL":
            case "TIME":
            case "TIME_OF_DAY":
            case "TOD":
                _currentByteOffset += 4;
                break;

            case "DATE_AND_TIME":
            case "DT":
            case "LREAL":
            case "LINT":
            case "ULINT":
            case "LWORD":
                _currentByteOffset += 8;
                break;

            case "DTL":
                _currentByteOffset += 12;
                break;

            default:
                // Bilinmeyen tipler için 1 byte
                _currentByteOffset++;
                break;
        }
    }

    /// <summary>
    /// EN: Parses default values from the BEGIN...END_DATA_BLOCK section.
    /// TR: BEGIN...END_DATA_BLOCK bölümünden varsayılan değerleri parse eder.
    /// </summary>
    private Dictionary<string, string> ParseDefaultValues(string content)
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var beginMatch = Regex.Match(content, @"\bBEGIN\b(.*?)\bEND_DATA_BLOCK\b", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!beginMatch.Success)
            return defaults;

        var section = beginMatch.Groups[1].Value;
        foreach (var rawLine in section.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                continue;

            // Format: "Name1"."Name2" := value;  veya  Name1.Name2 := value;
            var m = Regex.Match(line, @"^((?:(?:""[^""]* ""|[\w]+)\.?)+)\s*:=\s*(.+?)\s*;?\s*$");
            if (!m.Success)
                continue;

            // Key: tırnak işaretlerini kaldır, nokta ile birleştir
            var key = Regex.Replace(m.Groups[1].Value, @"""", "").Trim('.');
            var value = NormalizeDefaultValue(m.Groups[2].Value.Trim());

            defaults[key] = value;
        }

        return defaults;
    }

    private static string NormalizeDefaultValue(string value)
    {
        if (value.Equals("TRUE", StringComparison.OrdinalIgnoreCase))  return "true";
        if (value.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return "false";
        // String literal: 'metin' → metin
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1];
        return value;
    }

    /// <summary>
    /// EN: Converts the parse result to JSON format.
    /// TR: Parse sonucunu JSON formatına dönüştürür.
    /// </summary>
    public string ToJson(DbParseResult result, bool indented = true)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return JsonSerializer.Serialize(result, options);
    }

    /// <summary>
    /// EN: Creates a hierarchical symbol map (preserving struct structure).
    /// TR: Hiyerarşik sembol haritası oluşturur (struct yapısını koruyarak).
    /// </summary>
    public string ToHierarchicalJson(DbParseResult result, int dbNumber = 1, bool indented = true)
    {
        var hierarchicalMap = BuildHierarchicalStructure(result.Structure, dbNumber);

        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return JsonSerializer.Serialize(hierarchicalMap, options);
    }

    private Dictionary<string, object> BuildHierarchicalStructure(DbStructure structure, int dbNumber)
    {
        var result = new Dictionary<string, object>();

        foreach (var field in structure.Fields)
        {
            if (field.NestedStructure != null)
            {
                // Nested struct - recursive olarak işle
                result[field.Name] = new
                {
                    Type = "Struct",
                    Comment = field.Comment,
                    ByteOffset = field.ByteOffset,
                    BitOffset = field.BitOffset,
                    Fields = BuildHierarchicalStructure(field.NestedStructure, dbNumber)
                };
            }
            else
            {
                // Basit alan - fiziksel adres ve bilgilerle ekle
                string physicalAddress = GetPhysicalAddressFromField(field, dbNumber);
                string defaultValue = GetDefaultValue(field.Type);

                // DataType'a boyut bilgisini ekle (STRING/WSTRING için)
                string dataType = field.Type.ToUpperInvariant();
                if (field.Type.StartsWith("WString", StringComparison.OrdinalIgnoreCase))
                    dataType = $"WSTRING[{field.StringLength}]";
                else if (field.Type.StartsWith("String", StringComparison.OrdinalIgnoreCase))
                    dataType = $"STRING[{field.StringLength}]";

                result[field.Name] = new
                {
                    PhysicalAddress = physicalAddress,
                    DataType = dataType,
                    DefaultValue = defaultValue,
                    Description = field.Comment,
                    ByteOffset = field.ByteOffset,
                    BitOffset = field.BitOffset
                };
            }
        }

        return result;
    }

    /// <summary>
    /// EN: Creates a physical address from a DbField.
    /// TR: DbField'dan fiziksel adres oluşturur.
    /// </summary>
    private string GetPhysicalAddressFromField(DbField field, int dbNumber)
    {
        var upperFieldType = field.Type.ToUpperInvariant();

        if (upperFieldType.StartsWith("STRING") || upperFieldType.StartsWith("WSTRING"))
            return $"DB{dbNumber}.DBB{field.ByteOffset}";

        switch (upperFieldType)
        {
            case "BOOL":
                return $"DB{dbNumber}.DBX{field.ByteOffset}.{field.BitOffset}";

            case "BYTE":
            case "CHAR":
            case "SINT":
            case "USINT":
                return $"DB{dbNumber}.DBB{field.ByteOffset}";

            case "WORD":
            case "INT":
            case "UINT":
            case "WCHAR":
            case "DATE":
            case "S5TIME":
                return $"DB{dbNumber}.DBW{field.ByteOffset}";

            case "DWORD":
            case "DINT":
            case "UDINT":
            case "REAL":
            case "TIME":
            case "TIME_OF_DAY":
            case "TOD":
            case "DATE_AND_TIME":
            case "DT":
            case "LREAL":
            case "LINT":
            case "ULINT":
            case "LWORD":
            case "DTL":
                return $"DB{dbNumber}.DBD{field.ByteOffset}";

            default:
                return $"DB{dbNumber}.DBB{field.ByteOffset}";
        }
    }

    /// <summary>
    /// EN: Creates a simple symbol map from the parse result (for backward compatibility).
    /// TR: Parse sonucundan basit sembol haritası oluşturur (geriye dönük uyumluluk).
    /// </summary>
    public Dictionary<string, string> GenerateSymbolMap(DbParseResult result, int dbNumber = 1)
    {
        var fullMap = GenerateSymbolMapWithInfo(result, dbNumber);
        return fullMap.ToDictionary(x => x.Key, x => x.Value.PhysicalAddress);
    }

    /// <summary>
    /// EN: Creates a detailed symbol map from the parse result (with type, default value and description).
    /// TR: Parse sonucundan detaylı sembol haritası oluşturur (tip, varsayılan değer ve açıklama ile).
    /// </summary>
    public Dictionary<string, SymbolInfo> GenerateSymbolMapWithInfo(DbParseResult result, int dbNumber = 1)
    {
        var symbolMap = new Dictionary<string, SymbolInfo>();
        GenerateSymbolMapRecursiveWithInfo(result.Structure, "", symbolMap, dbNumber, result.DefaultValues);
        return symbolMap;
    }

    private void GenerateSymbolMapRecursive(DbStructure structure, string prefix, Dictionary<string, string> symbolMap, int dbNumber)
    {
        foreach (var field in structure.Fields)
        {
            var symbolicName = string.IsNullOrEmpty(prefix) 
                ? $"{field.Name}" 
                : $"{prefix}.{field.Name}";

            if (field.NestedStructure != null)
            {
                // Nested struct - recursive
                GenerateSymbolMapRecursive(field.NestedStructure, symbolicName, symbolMap, dbNumber);
            }
            else
            {
                // Fiziksel adres oluştur
                string physicalAddress = GetPhysicalAddressFromField(field, dbNumber);
                symbolMap[symbolicName] = physicalAddress;
            }
        }
    }

    private void GenerateSymbolMapRecursiveWithInfo(DbStructure structure, string prefix, Dictionary<string, SymbolInfo> symbolMap, int dbNumber, Dictionary<string, string>? defaults = null)
    {
        foreach (var field in structure.Fields)
        {
            var symbolicName = string.IsNullOrEmpty(prefix)
                ? field.Name
                : $"{prefix}.{field.Name}";

            if (field.NestedStructure != null)
            {
                // Nested struct - recursive
                GenerateSymbolMapRecursiveWithInfo(field.NestedStructure, symbolicName, symbolMap, dbNumber, defaults);
            }
            else
            {
                // Fiziksel adres oluştur
                string physicalAddress = GetPhysicalAddressFromField(field, dbNumber);

                // Önce tip bazlı varsayılan değeri al, sonra BEGIN bölümündeki gerçek değerle üzerine yaz
                string defaultValue = GetDefaultValue(field.Type);
                if (defaults != null && defaults.TryGetValue(symbolicName, out var parsedDefault))
                    defaultValue = parsedDefault;

                // DataType'a boyut bilgisini ekle (STRING/WSTRING için)
                string dataType = field.Type.ToUpperInvariant();
                if (field.Type.StartsWith("WString", StringComparison.OrdinalIgnoreCase))
                    dataType = $"WSTRING[{field.StringLength}]";
                else if (field.Type.StartsWith("String", StringComparison.OrdinalIgnoreCase))
                    dataType = $"STRING[{field.StringLength}]";

                symbolMap[symbolicName] = new SymbolInfo
                {
                    PhysicalAddress = physicalAddress,
                    DataType = dataType,
                    DefaultValue = defaultValue,
                    Description = field.Comment
                };
            }
        }
    }

    /// <summary>
    /// EN: Returns the default value for the given data type.
    /// TR: Veri tipine göre varsayılan değer döner.
    /// </summary>
    private string GetDefaultValue(string type)
    {
        return type.ToUpperInvariant() switch
        {
            "BOOL" => "false",
            "BYTE" or "CHAR" or "SINT" or "USINT" => "0",
            "WORD" or "INT" or "UINT" or "WCHAR" => "0",
            "S5TIME" or "DATE" => "0",
            "DWORD" or "DINT" or "UDINT" => "0",
            "REAL" or "LREAL" => "0.0",
            "TIME" or "TIME_OF_DAY" or "TOD" => "0",
            "DATE_AND_TIME" or "DT" or "DTL" => "1990-01-01 00:00:00",
            "LINT" or "ULINT" or "LWORD" => "0",
            "STRING" or "WSTRING" => "",
            _ => "0"
        };
    }
}

/// <summary>
/// EN: DB parse result.
/// TR: DB parse sonucu.
/// </summary>
public class DbParseResult
{
    public string DbName { get; set; } = "";
    public DbStructure Structure { get; set; } = new DbStructure();
    /// <summary>
    /// EN: Default values read from the BEGIN...END_DATA_BLOCK section.
    /// TR: BEGIN...END_DATA_BLOCK bölümünden okunan varsayılan değerler.
    /// </summary>
    public Dictionary<string, string> DefaultValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// EN: DB structure.
/// TR: DB yapısı.
/// </summary>
public class DbStructure
{
    public int Level { get; set; }
    public List<DbField> Fields { get; set; } = new List<DbField>();
}

/// <summary>
/// EN: DB field.
/// TR: DB alanı.
/// </summary>
public class DbField
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Comment { get; set; } = "";
    public int ByteOffset { get; set; }
    public int BitOffset { get; set; }
    public int StringLength { get; set; } = 254; // STRING tipi için boyut (varsayılan 254, toplam 256 byte)
    public DbStructure? NestedStructure { get; set; }
}
