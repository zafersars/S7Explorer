using S7.Net;
using System.Net.Sockets;

namespace S7Explorer;

/// <summary>
/// EN: Service class for communicating with Siemens S7 PLCs.
///     Provides connect, disconnect, read, and write operations.
///     Uses the S7.Net library internally.
/// TR: Siemens S7 PLC'lerle iletişim kuran servis sınıfı.
///     Bağlantı, okuma ve yazma işlemleri sağlar.
///     Dahili olarak S7.Net kütüphanesini kullanır.
/// </summary>
public class PlcService
{
    private Plc? _plc;
    private readonly SymbolMapper _symbolMapper;

    /// <summary>
    /// EN: Returns true if the PLC is currently connected.
    /// TR: PLC bağlı ise true döndürür.
    /// </summary>
    public bool IsConnected => _plc?.IsConnected ?? false;

    /// <summary>
    /// EN: CPU type of the connected PLC. Null if not connected.
    /// TR: Bağlı PLC'nin CPU tipi. Bağlı değilse null.
    /// </summary>
    public CpuType? ConnectedCpuType { get; private set; }

    /// <summary>
    /// EN: IP address of the connected PLC. Null if not connected.
    /// TR: Bağlı PLC'nin IP adresi. Bağlı değilse null.
    /// </summary>
    public string? ConnectedIpAddress { get; private set; }

    /// <summary>
    /// EN: Rack number of the connected PLC.
    /// TR: Bağlı PLC'nin rack numarası.
    /// </summary>
    public short ConnectedRack { get; private set; }

    /// <summary>
    /// EN: Slot number of the connected PLC.
    /// TR: Bağlı PLC'nin slot numarası.
    /// </summary>
    public short ConnectedSlot { get; private set; }

    /// <summary>
    /// EN: Fires when a status message is available (connection state changes, read/write results, errors).
    /// TR: Durum mesajı hazır olduğunda tetiklenir (bağlantı durumu, okuma/yazma sonuçları, hatalar).
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// EN: Access to the symbol map.
    /// TR: Sembol haritasına erişim.
    /// </summary>
    public SymbolMapper SymbolMapper => _symbolMapper;

    /// <summary>
    /// EN: Initializes a new instance of PlcService and sets up the symbol mapper.
    /// TR: PlcService'in yeni bir örneğini oluşturur ve sembol eşleyiciyi hazırlar.
    /// </summary>
    public PlcService()
    {
        _symbolMapper = new SymbolMapper();
    }

    /// <summary>
    /// EN: Attempts to connect to the PLC. Automatically retries with fallback slots on TPKT errors.
    /// TR: PLC'ye bağlanmayı dener. TPKT hatalarında alternatif slot değerleriyle otomatik yeniden dener.
    /// </summary>
    /// <param name="cpu">EN: CPU type. TR: CPU tipi.</param>
    /// <param name="ipAddress">EN: IP address of the PLC. TR: PLC'nin IP adresi.</param>
    /// <param name="rack">EN: Rack number (default 0). TR: Rack numarası (varsayılan 0).</param>
    /// <param name="slot">EN: Slot number (default 1). TR: Slot numarası (varsayılan 1).</param>
    /// <param name="cancellationToken">EN: Token to cancel the operation. TR: İşlemi iptal etmek için token.</param>
    /// <returns>EN: True if connected successfully. TR: Başarılı bağlanıldı ise true.</returns>
    public async Task<bool> ConnectAsync(CpuType cpu, string ipAddress, short rack = 0, short slot = 1, CancellationToken cancellationToken = default)
    {
        try
        {
            _plc = new Plc(cpu, ipAddress, rack, slot);
            StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_Trying", rack, slot));

            // OpenAsync'i iptal edilebilir Task ile sarmalayalım
            var openTask = _plc.OpenAsync(cancellationToken);
            var delayTask = Task.Delay(Timeout.Infinite, cancellationToken);
            var completedTask = await Task.WhenAny(openTask, delayTask);

            // Eğer iptal edildi ise
            if (completedTask == delayTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // OpenAsync tamamlandı, sonucunu bekle
            await openTask;

            if (IsConnected)
            {
                // Bağlantı bilgilerini sakla
                ConnectedCpuType = cpu;
                ConnectedIpAddress = ipAddress;
                ConnectedRack = rack;
                ConnectedSlot = slot;

                StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_ConnectSuccess"));
            }
            else
            {
                StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_ConnectFailed"));
            }

            return IsConnected;
        }
        catch (OperationCanceledException)
        {
            // Bağlantı iptal edildi
            _plc?.Close();
            _plc = null;
            StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_ConnectCancelled"));
            throw;
        }
        catch (SocketException ex)
        {
            StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_ConnectError", ex.Message));
            return false;
        }
        catch (Exception ex) when (ex.Message.Contains("TPKT") && !cancellationToken.IsCancellationRequested)
        {
            // TPKT hatası alındıysa farklı Slot değerleriyle otomatik yeniden dene
            short[] fallbackSlots = slot == 1 ? [0, 2] : (slot == 0 ? [1, 2] : [0, 1]);
            foreach (var fallbackSlot in fallbackSlots)
            {
                StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_TpktRetry", fallbackSlot));
                try
                {
                    _plc?.Close();
                    _plc = new Plc(cpu, ipAddress, rack, fallbackSlot);
                    await _plc.OpenAsync(cancellationToken);
                    if (_plc.IsConnected)
                    {
                        ConnectedCpuType = cpu;
                        ConnectedIpAddress = ipAddress;
                        ConnectedRack = rack;
                        ConnectedSlot = fallbackSlot;
                        StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_ConnectSuccessSlot", fallbackSlot));
                        return true;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* bir sonraki slot'u dene */ }
            }
            StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_TpktError"));
            return false;
        }
    }
    /// <summary>
    /// EN: Disconnects from the PLC and clears all connection information.
    /// TR: PLC bağlantısını keser ve tüm bağlantı bilgilerini temizler.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_plc != null)
        {
            _plc.Close();

            // Bağlantı bilgilerini temizle
            ConnectedCpuType = null;
            ConnectedIpAddress = null;
            ConnectedRack = 0;
            ConnectedSlot = 0;

            StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_Disconnected"));
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// EN: Reads the value at the specified address (symbolic or physical) from the PLC.
    ///     Automatically converts the raw value to the correct .NET type based on the symbol data type.
    /// TR: Belirtilen adresin (sembolik veya fiziksel) değerini PLC'den okur.
    ///     Ham değeri sembol veri tipine göre otomatik olarak doğru .NET tipine dönüştürür.
    /// </summary>
    /// <param name="variable">EN: Symbolic or physical address to read. TR: Okunacak sembolik veya fiziksel adres.</param>
    /// <returns>EN: The read and converted value. TR: Okunan ve dönüştürülen değer.</returns>
    public async Task<object?> ReadAsync(string variable)
    {
        if (_plc == null || !IsConnected)
            throw new InvalidOperationException(LocalizationManager.Instance.T("Ex_PlcNotConnected"));

        try
        {
            // Sembol bilgisini al
            var symbolInfo = _symbolMapper.GetSymbolInfo(variable);
            var physicalAddress = symbolInfo?.PhysicalAddress ?? _symbolMapper.Resolve(variable);
            var dataType = symbolInfo?.DataType ?? string.Empty;

            // 8/12-byte tipler: S7.Net DBQ desteklemiyor › ReadBytesAsync kullan
            object? result;
            if (dataType.ToUpperInvariant() is "DATE_AND_TIME" or "DT" or "LINT" or "ULINT" or "LWORD" or "LREAL")
                result = await Read8BytesAsUlongAsync(physicalAddress);
            else if (dataType.ToUpperInvariant() == "DTL")
                result = await ReadDtlBytesAsync(physicalAddress);
            else if (dataType.StartsWith("STRING", StringComparison.OrdinalIgnoreCase))
                result = await ReadStringAsync(physicalAddress, dataType);
            else if (dataType.StartsWith("WSTRING", StringComparison.OrdinalIgnoreCase))
                result = await ReadWStringAsync(physicalAddress, dataType);
            else
                result = await _plc.ReadAsync(physicalAddress);

            // Okunan ham değeri sembol tipine göre dönüştür
            var convertedResult = ConvertReadValue(dataType, result);

            StatusChanged?.Invoke(this, variable != physicalAddress
                ? LocalizationManager.Instance.T("Plc_ReadDoneWithAddr", variable, physicalAddress)
                : LocalizationManager.Instance.T("Plc_ReadDone", variable));

            return convertedResult;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_ReadError", ex.Message));
            throw;
        }
    }

    /// <summary>
    /// EN: Writes the specified value to the given address (symbolic or physical) on the PLC.
    ///     Automatically converts the value to the correct PLC data type.
    /// TR: Belirtilen değeri PLC'deki adrese (sembolik veya fiziksel) yazar.
    ///     Değeri otomatik olarak doğru PLC veri tipine dönüştürür.
    /// </summary>
    /// <param name="variable">EN: Symbolic or physical address to write. TR: Yazılacak sembolik veya fiziksel adres.</param>
    /// <param name="value">EN: Value to write. TR: Yazılacak değer.</param>
    public async Task WriteAsync(string variable, object value)
    {
        if (_plc == null || !IsConnected)
            throw new InvalidOperationException(LocalizationManager.Instance.T("Ex_PlcNotConnected"));

        try
        {
            // Sembol bilgisini al
            var symbolInfo = _symbolMapper.GetSymbolInfo(variable);
            var physicalAddress = symbolInfo?.PhysicalAddress ?? _symbolMapper.Resolve(variable);
            var dataType = symbolInfo?.DataType ?? string.Empty;

            // 8/12-byte tipler: S7.Net DBQ desteklemiyor › WriteBytesAsync kullan
            if (dataType.ToUpperInvariant() is "DATE_AND_TIME" or "DT" or "LINT" or "ULINT" or "LWORD" or "LREAL")
            {
                await Write8BytesAsync(physicalAddress, dataType, value);
                StatusChanged?.Invoke(this, variable != physicalAddress
                    ? LocalizationManager.Instance.T("Plc_WriteDoneWithAddr", variable, physicalAddress, value)
                    : LocalizationManager.Instance.T("Plc_WriteDone", variable, value));
                return;
            }
            if (dataType.ToUpperInvariant() == "DTL")
            {
                await WriteDtlBytesAsync(physicalAddress, value);
                StatusChanged?.Invoke(this, variable != physicalAddress
                    ? LocalizationManager.Instance.T("Plc_WriteDoneWithAddr", variable, physicalAddress, value)
                    : LocalizationManager.Instance.T("Plc_WriteDone", variable, value));
                return;
            }

            if (dataType.StartsWith("STRING", StringComparison.OrdinalIgnoreCase))
            {
                await WriteStringAsync(physicalAddress, dataType, value?.ToString() ?? "");
                StatusChanged?.Invoke(this, variable != physicalAddress
                    ? LocalizationManager.Instance.T("Plc_WriteDoneWithAddr", variable, physicalAddress, value)
                    : LocalizationManager.Instance.T("Plc_WriteDone", variable, value));
                return;
            }

            if (dataType.StartsWith("WSTRING", StringComparison.OrdinalIgnoreCase))
            {
                await WriteWStringAsync(physicalAddress, dataType, value?.ToString() ?? "");
                StatusChanged?.Invoke(this, variable != physicalAddress
                    ? LocalizationManager.Instance.T("Plc_WriteDoneWithAddr", variable, physicalAddress, value)
                    : LocalizationManager.Instance.T("Plc_WriteDone", variable, value));
                return;
            }

            // Veri tipine göre değeri dönüştür
            var convertedValue = ConvertValueForDataType(physicalAddress, dataType, value);

            await _plc.WriteAsync(physicalAddress, convertedValue);

            StatusChanged?.Invoke(this, variable != physicalAddress
                ? LocalizationManager.Instance.T("Plc_WriteDoneWithAddr", variable, physicalAddress, convertedValue)
                : LocalizationManager.Instance.T("Plc_WriteDone", variable, convertedValue));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, LocalizationManager.Instance.T("Plc_WriteError", ex.Message));
            throw;
        }
    }

    /// <summary>
    /// EN: Converts the raw read value according to the symbol type.
    /// TR: Okunan ham değeri sembol tipine göre dönüştürür.
    /// </summary>
    private static object? ConvertReadValue(string dataType, object? value)
    {
        if (value == null || string.IsNullOrEmpty(dataType))
            return value;

        var upper = dataType.ToUpperInvariant();

        // SINT: PLC'den byte gelir (0-255) › .NET sbyte (-128..127)
        if (upper == "SINT")
            return value is byte bSint ? (sbyte)bSint : value;

        // INT: PLC'den ushort gelir (0-65535) › .NET short (-32768..32767)
        if (upper == "INT")
            return value is ushort usInt ? (short)usInt : value;

        // DINT: PLC'den uint gelir › .NET int
        if (upper == "DINT")
            return value is uint uiDint ? (int)uiDint : value;

        // LINT: PLC'den ulong gelir › .NET long
        if (upper == "LINT")
            return value is ulong ulLint ? (long)ulLint : value;

        // REAL: PLC'den uint gelir (ham IEEE 754 bit deseni) › .NET float
        if (upper == "REAL")
            return value is uint uiReal ? BitConverter.Int32BitsToSingle((int)uiReal) : value;

        // LREAL: PLC'den ulong gelir (ham IEEE 754 bit deseni) › .NET double
        if (upper == "LREAL")
            return value is ulong ulLreal ? BitConverter.Int64BitsToDouble((long)ulLreal) : value;

        // CHAR: PLC'den byte gelir (0-255) › .NET char
        if (upper == "CHAR")
            return value is byte b ? (char)b : value;

        // WCHAR: PLC'den ushort gelir (Unicode code point) › .NET char
        if (upper == "WCHAR")
            return value is ushort us ? (char)us : value;

        // TIME: uint/int (ms) › "T#..." okunabilir format
        if (upper == "TIME")
        {
            if (value is uint tuInt) return ConvertFromTime((int)tuInt);
            if (value is int  tiInt) return ConvertFromTime(tiInt);
            return value;
        }

        // S5TIME: ushort (BCD kodlu) › "S5T#..." okunabilir format
        if (upper == "S5TIME")
            return value is ushort usS5 ? ConvertFromS5Time(usS5) : value;

        // DATE: ushort (1990-01-01'den gün sayısı) › tarih formatı
        if (upper == "DATE")
            return value is ushort days ? new DateTime(1990, 1, 1).AddDays(days).ToString("yyyy-MM-dd") : value;

        // TIME_OF_DAY: uint (gece yarısından ms) › saat formatı
        if (upper is "TIME_OF_DAY" or "TOD")
        {
            var todMs = value is uint todU ? todU : value is int todI ? (uint)todI : uint.MaxValue;
            return todMs != uint.MaxValue ? TimeSpan.FromMilliseconds(todMs).ToString(@"hh\:mm\:ss\.fff") : value;
        }

        // DATE_AND_TIME / DT: PLC'den ulong gelir (8 BCD byte) › "DT#..." formatı
        if (upper is "DATE_AND_TIME" or "DT")
            return value is ulong ulDt ? ConvertFromDateAndTime(ulDt) : value;

        // DTL: PLC'den byte[12] gelir › "DTL#..." formatı
        if (upper == "DTL")
            return value is byte[] dtlBytes ? ConvertFromDtl(dtlBytes) : value;

        return value;
    }

    /// <summary>
    /// EN: Converts the value to the appropriate type based on the data type and physical address.
    /// TR: Veri tipine ve fiziksel adrese göre değeri uygun tipe dönüştürür.
    /// </summary>
    private static object ConvertValueForDataType(string physicalAddress, string dataType, object value)
    {
        // Öncelikle sembol tipine göre
        if (!string.IsNullOrEmpty(dataType))
        {
            // STRING[xx] ve WSTRING[xx] formatlarını kontrol et
            if (dataType.StartsWith("STRING", StringComparison.OrdinalIgnoreCase) ||
                dataType.StartsWith("WSTRING", StringComparison.OrdinalIgnoreCase))
            {
                return value?.ToString() ?? string.Empty;
            }

            return dataType.ToUpperInvariant() switch
            {
                "BOOL"              => ConvertToBool(value),
                "CHAR"              => (byte)ConvertToChar(value),
                "WCHAR"             => (ushort)ConvertToChar(value),
                "BYTE" or "USINT"   => Convert.ToByte(value),
                "SINT"              => (byte)Convert.ToSByte(value),
                "WORD"              => Convert.ToUInt16(value),          // 0..65535
                "INT"               => (ushort)Convert.ToInt16(value),   // -32768..32767, bit deseni korunur
                "UINT"              => Convert.ToUInt16(value),
                "DWORD"             => Convert.ToUInt32(value),          // 0..4294967295
                "DINT"              => (uint)Convert.ToInt32(value),     // -2147483648..2147483647, bit deseni korunur
                "UDINT"             => Convert.ToUInt32(value),
                "REAL"              => Convert.ToSingle(value),
                // S7-1500: 8-byte tipler
                "LINT"              => (ulong)Convert.ToInt64(value),    // bit deseni korunur
                "ULINT" or "LWORD"  => Convert.ToUInt64(value),
                "LREAL"             => Convert.ToDouble(value),
                // Zaman ve sayıç tipleri
                "TIME"                                => (uint)ConvertToTime(value),   // T#... veya ms
                "S5TIME"                              => ConvertToS5Time(value),
                "DATE"                                => ConvertToDate(value),
                "TIME_OF_DAY" or "TOD"                => ConvertToTimeOfDay(value),
                "DATE_AND_TIME" or "DT"               => ConvertToDateTimeUlong(value),
                "DTL"                                 => ConvertToDateAndTime(value),
                "COUNTER"                             => Convert.ToUInt16(value),
                _                                     => value
            };
        }

        // Sembol tipi yoksa fiziksel adres formatına göre
        return ConvertValueForAddress(physicalAddress, value);
    }

    /// <summary>
    /// EN: Converts the value to the appropriate type based on the physical address format.
    /// TR: Fiziksel adres formatına göre değeri uygun tipe dönüştürür.
    /// </summary>
    private static object ConvertValueForAddress(string physicalAddress, object value)
    {
        // Bit adresi kontrolü (DBX, I.x, Q.x, M.x formatları)
        if (IsBitAddress(physicalAddress))
        {
            return ConvertToBool(value);
        }

        // Byte adresi (DBB) - STRING veya BYTE olabilir
        if (physicalAddress.Contains("DBB", StringComparison.OrdinalIgnoreCase))
        {
            // Eğer değer string ise, STRING olarak kabul et
            if (value is string)
            {
                return value;
            }
            // Değilse BYTE olarak
            return Convert.ToByte(value);
        }

        // Word adresi (DBW) - tam 16-bit aralık: ushort (0..65535)
        if (physicalAddress.Contains("DBW", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToUInt16(value);
        }

        // DWord adresi (DBD) - Real veya tam 32-bit: uint (0..4294967295)
        if (physicalAddress.Contains("DBD", StringComparison.OrdinalIgnoreCase))
        {
            // Ondalıklı değer ise Real (float) olarak
            if (value is float or double)
                return Convert.ToSingle(value);
            // Değilse uint olarak (DWORD/DINT her ikisini de kapsar)
            return Convert.ToUInt32(value);
        }

        // S7-1500: QWord adresi (DBQ) - LReal, LInt, ULInt, LWord
        if (physicalAddress.Contains("DBQ", StringComparison.OrdinalIgnoreCase))
        {
            // Ondalıklı değer ise LReal (double) olarak
            if (value is float or double)
                return Convert.ToDouble(value);
            // Değilse ulong olarak (LINT/ULINT/LWORD her ikisini de kapsar)
            return Convert.ToUInt64(value);
        }

        // Merker (M), Input (I/E=Eingang), Output (Q/A=Ausgang) adresleri
        // xB › byte (1 byte) : MB2, IB0, QB0, EB0, AB0
        // xW › ushort (2 byte): MW4, IW0, QW0, EW0, AW0
        // xD › uint/float (4 byte): MD6, ID0, QD0, ED0, AD0
        // x{digit} › bit adresleri IsBitAddress tarafından daha önce yakalanır (M0.0, E0.0 vb.)
        var upAddr = physicalAddress.ToUpperInvariant().Trim();
        if (upAddr.Length >= 2 && "MIQEA".Contains(upAddr[0]))
        {
            return upAddr[1] switch
            {
                'B' => Convert.ToByte(value),
                'W' => Convert.ToUInt16(value),
                'D' when value is float or double => Convert.ToSingle(value),
                'D' => Convert.ToUInt32(value),
                _ => value
            };
        }

        // Timer (T0..T255) ve Counter (C0..C255 veya Z0..Z255) — S7-300/400 ushort değeri
        // S7-1500 IEC timer/counter'ları DB içindedir, buraya gelmez
        if (upAddr.Length >= 2 && upAddr[0] is 'T' or 'C' or 'Z' && char.IsDigit(upAddr[1]))
            return Convert.ToUInt16(value);

        // Varsayılan: değeri olduğu gibi dön
        return value;
    }

    /// <summary>
    /// EN: Checks whether the address is a bit address.
    /// TR: Adresin bit adresi olup olmadığını kontrol eder.
    /// </summary>
    private static bool IsBitAddress(string address)
    {
        // DBX formatı (ör: DB1.DBX0.0)
        if (address.Contains("DBX", StringComparison.OrdinalIgnoreCase))
            return true;

        var upper = address.ToUpperInvariant();

        // I/E (Input/Eingang), Q/A (Output/Ausgang), M (Merker) bit formatları
        // Ör: I0.0, E0.0, Q1.5, A1.5, M2.7
        if ((upper.StartsWith('I') || upper.StartsWith('Q') || upper.StartsWith('M') ||
             upper.StartsWith('E') || upper.StartsWith('A')) &&
            address.Contains('.'))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// EN: Converts a DATE value to ushort (days since 1990-01-01). Input: "2024-01-15" or numeric day value.
    /// TR: DATE değerini ushort'a dönüştürür (1990-01-01'den gün sayısı). Giriş: "2024-01-15" veya sayısal gün değeri.
    /// </summary>
    private static ushort ConvertToDate(object value)
    {
        if (value is string s && DateTime.TryParse(s, out var dt))
            return (ushort)Math.Max(0, (dt.Date - new DateTime(1990, 1, 1)).Days);
        return Convert.ToUInt16(value);
    }

    /// <summary>
    /// EN: Converts a TIME_OF_DAY value to uint (milliseconds since midnight). Input: "12:30:00" or ms value.
    /// TR: TIME_OF_DAY değerini uint'e dönüştürür (gece yarısından ms). Giriş: "12:30:00" veya ms değeri.
    /// </summary>
    private static uint ConvertToTimeOfDay(object value)
    {
        if (value is string s && TimeSpan.TryParse(s, out var ts))
            return (uint)ts.TotalMilliseconds;
        return Convert.ToUInt32(value);
    }

    /// <summary>
    /// EN: Converts a DATE_AND_TIME / DT / DTL value to DateTime. Input: "2024-01-15 12:30:00" format.
    /// TR: DATE_AND_TIME / DT / DTL değerini DateTime'a dönüştürür. Giriş: "2024-01-15 12:30:00" formatı.
    /// </summary>
    private static object ConvertToDateAndTime(object value)
    {
        if (value is DateTime dt) return dt;
        if (value is string s && DateTime.TryParse(s, out var parsed)) return parsed;
        return value;
    }

    /// <summary>
    /// EN: Converts a value to bool (for bit writing). Rules: 'true'/'false' strings accepted; 0=false, 1=true; any number other than 1=false.
    /// TR: Değeri bool'a dönüştürür (bit yazma için). Kurallar: 'true'/'false' string değerleri kabul edilir; 0=false, 1=true; 1 dışındaki sayılar=false.
    /// </summary>
    private static bool ConvertToBool(object value)
    {
        // Zaten bool ise
        if (value is bool b)
            return b;

        // String ise - önce 'true'/'false' kontrol et
        if (value is string str)
        {
            var trimmedStr = str.Trim();

            // 'true' veya 'false' string değerleri
            if (bool.TryParse(trimmedStr, out var boolResult))
                return boolResult;

            // Sayısal string değerleri: 0 veya 1
            if (int.TryParse(trimmedStr, out var intResult))
                return intResult == 1; // Sadece 1 ise true, diğer tüm sayılar false

            if (double.TryParse(trimmedStr, out var doubleResult))
                return doubleResult == 1.0; // Sadece 1.0 ise true
        }

        // Sayısal tipler - sadece tam olarak 1 ise true
        if (value is int i)
            return i == 1;
        if (value is double d)
            return d == 1.0;
        if (value is float f)
            return f == 1.0f;
        if (value is decimal dec)
            return dec == 1m;
        if (value is long l)
            return l == 1L;
        if (value is ulong ul)
            return ul == 1UL;
        if (value is byte by)
            return by == 1;
        if (value is short sh)
            return sh == 1;

        // Varsayılan: false
        return false;
    }

    /// <summary>
    /// EN: Converts a value to char (for CHAR / WCHAR writing).
    /// TR: Değeri char'a dönüştürür (CHAR / WCHAR yazma için).
    /// </summary>
    private static char ConvertToChar(object value)
    {
        if (value is char c)                      return c;
        if (value is string s && s.Length > 0)    return s[0];
        return '\0';
    }

        /// <summary>
        /// EN: Converts milliseconds to "T#..." format. Example: 1500 -> "T#1s_500ms", 90000 -> "T#1m_30s".
        /// TR: Milisaniyeyi "T#..." formatına dönüştürür. Örnek: 1500 -> "T#1s_500ms", 90000 -> "T#1m_30s".
        /// </summary>
        private static string ConvertFromTime(int ms)
    {
        if (ms == 0) return "T#0ms";

        bool negative = ms < 0;
        long rem = Math.Abs((long)ms);

        long d = rem / 86_400_000; rem %= 86_400_000;
        long h = rem /  3_600_000; rem %=  3_600_000;
        long m = rem /     60_000; rem %=     60_000;
        long s = rem /      1_000; rem %=      1_000;

        var result = (negative ? "T#-" : "T#")
            + (d   > 0 ? $"{d}d_"   : "")
            + (h   > 0 ? $"{h}h_"   : "")
            + (m   > 0 ? $"{m}m_"   : "")
            + (s   > 0 ? $"{s}s_"   : "")
            + (rem > 0 ? $"{rem}ms" : "");

        return result.TrimEnd('_');
    }

    /// <summary>
    /// EN: Returns values like "T#150ms", "T#1m_30s" as milliseconds (int).
    /// TR: "T#150ms", "T#1m_30s" gibi değeri milisaniye (int) olarak döndürür.
    /// </summary>
    private static int ConvertToTime(object value)
    {
        if (value is string s) return ParseTimeToMs(s);
        return Convert.ToInt32(value);
    }

    /// <summary>
    /// EN: Converts a string in "T#1m_30s_500ms" format to milliseconds.
    /// TR: "T#1m_30s_500ms" formatındaki string'i milisaniyeye dönüştürür.
    /// </summary>
    private static int ParseTimeToMs(string s)
    {
        s = s.Trim();
        if (s.StartsWith("T#", StringComparison.OrdinalIgnoreCase))
            s = s[2..];
        else if (s.StartsWith("TIME#", StringComparison.OrdinalIgnoreCase))
            s = s[5..];

        if (int.TryParse(s, out var plainMs))
            return plainMs;

        bool negative = s.StartsWith('-');
        if (negative) s = s[1..];

        s = s.Replace("_", "").ToLowerInvariant();
        long total = 0;
        int i = 0;
        while (i < s.Length)
        {
            int start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == start || !long.TryParse(s[start..i], out var num)) break;

            if (i + 1 < s.Length && s[i] == 'm' && s[i + 1] == 's')
            { total += num;             i += 2; } // milisaniye
            else if (i < s.Length && s[i] == 'd')
            { total += num * 86_400_000; i++; }  // gün
            else if (i < s.Length && s[i] == 'h')
            { total += num *  3_600_000; i++; }  // saat
            else if (i < s.Length && s[i] == 'm')
            { total += num *     60_000; i++; }  // dakika
            else if (i < s.Length && s[i] == 's')
            { total += num *      1_000; i++; }  // saniye
            else
            { total += num; break; }             // birim yok › ms
        }
        return (int)(negative ? -total : total);
    }

    /// <summary>
    /// EN: Converts an S5TIME ushort (BCD) value to "S5T#..." format. Example: 0x0013 -> "S5T#130ms".
    /// TR: S5TIME ushort (BCD) değerini "S5T#..." formatına dönüştürür. Örnek: 0x0013 -> "S5T#130ms".
    /// </summary>
    private static string ConvertFromS5Time(ushort value)
    {
        int timeBase = (value >> 12) & 0x3;
        int hDigit   = (value >> 8)  & 0xF;
        int tDigit   = (value >> 4)  & 0xF;
        int oDigit   =  value        & 0xF;

        if (hDigit > 9 || tDigit > 9 || oDigit > 9)
            return value.ToString(); // geçersiz BCD

        long bcdVal  = hDigit * 100 + tDigit * 10 + oDigit;
        long[] bases = { 10, 100, 1_000, 10_000 };
        long totalMs = bcdVal * bases[timeBase];

        if (totalMs == 0) return "S5T#0ms";

        long rem = totalMs;
        long h   = rem / 3_600_000; rem %= 3_600_000;
        long m   = rem / 60_000;    rem %= 60_000;
        long s   = rem / 1_000;     rem %= 1_000;

        var result = "S5T#"
            + (h   > 0 ? $"{h}h_"   : "")
            + (m   > 0 ? $"{m}m_"   : "")
            + (s   > 0 ? $"{s}s_"   : "")
            + (rem > 0 ? $"{rem}ms" : "");

        return result.TrimEnd('_');
    }

    /// <summary>
    /// EN: Encodes values like "S5T#125ms", "S5T#1m_30s" to S5TIME ushort (BCD) format.
    /// TR: "S5T#125ms", "S5T#1m_30s" gibi değeri S5TIME ushort (BCD) formatına kodlar.
    /// </summary>
    private static ushort ConvertToS5Time(object value)
    {
        long ms = value is string str ? ParseS5TimeToMs(str) : Convert.ToInt64(value);
        return EncodeS5Time(ms);
    }

    /// <summary>
    /// EN: Converts a string in "S5T#1m_30s_500ms" format to milliseconds.
    /// TR: "S5T#1m_30s_500ms" formatındaki string'i milisaniyeye dönüştürür.
    /// </summary>
    private static long ParseS5TimeToMs(string s)
    {
        s = s.Trim();
        if (s.StartsWith("S5T#", StringComparison.OrdinalIgnoreCase))
            s = s[4..];
        else if (s.StartsWith("S5TIME#", StringComparison.OrdinalIgnoreCase))
            s = s[7..];

        if (long.TryParse(s, out var plainMs))
            return plainMs;

        s = s.Replace("_", "").ToLowerInvariant();
        long total = 0;
        int i = 0;
        while (i < s.Length)
        {
            int start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == start || !long.TryParse(s[start..i], out var num)) break;

            if (i + 1 < s.Length && s[i] == 'm' && s[i + 1] == 's')
            { total += num;           i += 2; }  // milisaniye
            else if (i < s.Length && s[i] == 'h')
            { total += num * 3_600_000; i++; }   // saat
            else if (i < s.Length && s[i] == 'm')
            { total += num * 60_000;    i++; }   // dakika
            else if (i < s.Length && s[i] == 's')
            { total += num * 1_000;     i++; }   // saniye
            else
            { total += num; break; }             // birim yok › ms
        }
        return total;
    }

    /// <summary>
    /// EN: Encodes milliseconds to S5TIME BCD ushort value. Selects the smallest suitable time base using 3 BCD digits.
    /// TR: Milisaniyeyi S5TIME BCD ushort değerine kodlar. En küçük uygun time base seçilerek 3 BCD hanesi kullanılır.
    /// </summary>
    private static ushort EncodeS5Time(long ms)
    {
        if (ms <= 0) return 0;
        ms = Math.Min(ms, 9_990_000L); // max: 999 × 10s = 9990s

        int[] basesMs = { 10, 100, 1_000, 10_000 };
        for (int bi = 0; bi < basesMs.Length; bi++)
        {
            long bcdVal = (ms + basesMs[bi] / 2) / basesMs[bi]; // yuvarla
            if (bcdVal <= 999)
            {
                int h = (int)(bcdVal / 100);
                int t = (int)((bcdVal % 100) / 10);
                int o = (int)(bcdVal % 10);
                return (ushort)((bi << 12) | (h << 8) | (t << 4) | o);
            }
        }
        return 0x3999; // maksimum değer
    }

    /// <summary>
    /// S7 DATE_AND_TIME 8-byte BCD ulong değerini "DT#yyyy-MM-dd-HH:mm:ss.fff" formatına dönüştürür.
    /// Byte düzeni (big-endian): [Yıl][Ay][Gün][Saat][Dak][San][Ms_onlar][Ms_birler(üst nibble)|HGünü(alt nibble)]
    /// Yıl 90-99 › 1990-1999, 00-89 › 2000-2089
    /// </summary>
    private static string ConvertFromDateAndTime(ulong raw)
    {
        int year  = BcdToDec((byte)(raw >> 56));
        year = year >= 90 ? 1900 + year : 2000 + year;
        int month = BcdToDec((byte)(raw >> 48));
        int day   = BcdToDec((byte)(raw >> 40));
        int hour  = BcdToDec((byte)(raw >> 32));
        int min   = BcdToDec((byte)(raw >> 24));
        int sec   = BcdToDec((byte)(raw >> 16));
        int msHi  = BcdToDec((byte)(raw >>  8));   // ms'nin yüzler ve onlar basamağı
        int msLo  = (int)((raw & 0xF0) >> 4);      // Byte 7 üst nibble = ms birler basamağı
        int ms    = msHi * 10 + msLo;

        try
        {
            var dt = new DateTime(year, Math.Max(1, month), Math.Max(1, day),
                                  hour, min, sec, Math.Min(999, ms));
            return $"DT#{dt:yyyy-MM-dd-HH:mm:ss.fff}";
        }
        catch
        {
            return $"DT#{year:D4}-{month:D2}-{day:D2}-{hour:D2}:{min:D2}:{sec:D2}.{ms:D3}";
        }
    }

    /// <summary>
    /// EN: Encodes a "DT#yyyy-MM-dd-HH:mm:ss" string or DateTime value to an S7 DATE_AND_TIME 8-byte BCD ulong.
    ///     Accepted formats: "DT#1990-01-01-00:00:00", "1990-01-01 00:00:00", DateTime object.
    /// TR: "DT#yyyy-MM-dd-HH:mm:ss" veya DateTime değerini S7 DATE_AND_TIME 8-byte BCD ulong'una kodlar.
    ///     Kabul edilen formatlar: "DT#1990-01-01-00:00:00", "1990-01-01 00:00:00", DateTime nesnesi.
    /// </summary>
    private static ulong ConvertToDateTimeUlong(object value)
    {
        DateTime dt;
        if (value is DateTime dtv)
        {
            dt = dtv;
        }
        else if (value is string s)
        {
            var clean = s.Trim();
            if (clean.StartsWith("DT#", StringComparison.OrdinalIgnoreCase))
                clean = clean[3..];
            // TIA Portal formatı: "1990-01-01-00:00:00" › son tire tarih-saat ayracı
            var parts = clean.Split('-');
            if (parts.Length == 4)
                clean = $"{parts[0]}-{parts[1]}-{parts[2]} {parts[3]}";
            if (!DateTime.TryParse(clean, out dt))
                return 0;
        }
        else
        {
            try { dt = Convert.ToDateTime(value); }
            catch { return 0; }
        }

        int year2 = dt.Year >= 2000 ? dt.Year - 2000 : dt.Year - 1900;
        // S7 HGünü: 1=Pazar, 2=Pazartesi, ..., 7=Cumartesi
        int dow   = (int)dt.DayOfWeek; // 0=Sun
        int s7dow = dow == 0 ? 1 : dow + 1;

        ulong raw = 0;
        raw |= (ulong)DecToBcd(year2)                        << 56;
        raw |= (ulong)DecToBcd(dt.Month)                     << 48;
        raw |= (ulong)DecToBcd(dt.Day)                       << 40;
        raw |= (ulong)DecToBcd(dt.Hour)                      << 32;
        raw |= (ulong)DecToBcd(dt.Minute)                    << 24;
        raw |= (ulong)DecToBcd(dt.Second)                    << 16;
        raw |= (ulong)DecToBcd(dt.Millisecond / 10)          <<  8; // ms yüzler+onlar (BCD)
        raw |= (ulong)(((dt.Millisecond % 10) << 4) | s7dow);       // ms birler nibble | HGünü nibble
        return raw;
    }

    /// <summary>
    /// EN: Converts a BCD byte to decimal: 0x90 -> 90.
    /// TR: BCD baytını decimal'e dönüştürür: 0x90 -> 90.
    /// </summary>
    private static int  BcdToDec(byte bcd) => (bcd >> 4) * 10 + (bcd & 0xF);

    /// <summary>
    /// EN: Converts a decimal number to a BCD byte: 90 -> 0x90.
    /// TR: Decimal sayıyı BCD baytına dönüştürür: 90 -> 0x90.
    /// </summary>
    private static byte DecToBcd(int dec)  => (byte)(((dec / 10) << 4) | (dec % 10));

    /// <summary>
    /// EN: Reads 8 bytes from the PLC for 8-byte types (LINT/ULINT/LWORD/LREAL/DATE_AND_TIME/DT) and returns them as big-endian ulong.
    ///     Uses ReadBytesAsync since S7.Net does not support DBQ string format.
    /// TR: 8-byte tipler (LINT/ULINT/LWORD/LREAL/DATE_AND_TIME/DT) için PLC'den 8 byte okur, big-endian ulong döndürür.
    ///     S7.Net DBQ string formatı desteklemediğinden ReadBytesAsync kullanılır.
    /// </summary>
    private async Task<ulong> Read8BytesAsUlongAsync(string physicalAddress)
    {
        var (db, offset) = ParseDbBlockAddress(physicalAddress);
        var bytes = await _plc!.ReadBytesAsync(DataType.DataBlock, db, offset, 8);
        ulong raw = 0;
        for (int i = 0; i < 8; i++)
            raw = (raw << 8) | bytes[i];
        return raw;
    }

    /// <summary>
    /// EN: Writes 8-byte types (LINT/ULINT/LWORD/LREAL/DATE_AND_TIME/DT) as big-endian byte[8] to the PLC.
    ///     LINT: int64 bit pattern, LREAL: IEEE 754 double bit pattern, DT: BCD encoded.
    /// TR: 8-byte tipleri (LINT/ULINT/LWORD/LREAL/DATE_AND_TIME/DT) big-endian byte[8] olarak PLC'ye yazar.
    ///     LINT: int64 bit deseni, LREAL: IEEE 754 double bit deseni, DT: BCD kodlu.
    /// </summary>
    private async Task Write8BytesAsync(string physicalAddress, string dataType, object value)
    {
        ulong raw = dataType.ToUpperInvariant() switch
        {
            "DATE_AND_TIME" or "DT" => ConvertToDateTimeUlong(value),
            "LINT"                  => (ulong)Convert.ToInt64(value),
            "LREAL"                 => (ulong)BitConverter.DoubleToInt64Bits(Convert.ToDouble(value)),
            _                       => Convert.ToUInt64(value),  // ULINT, LWORD
        };
        var bytes = new byte[8];
        for (int i = 7; i >= 0; i--)
        {
            bytes[i] = (byte)(raw & 0xFF);
            raw >>= 8;
        }
        var (db, offset) = ParseDbBlockAddress(physicalAddress);
        await _plc!.WriteBytesAsync(DataType.DataBlock, db, offset, bytes);
    }

    /// <summary>
    /// EN: Extracts the DB number and byte offset from an address in "DB{n}.DB{X}{offset}" format.
    ///     Example: "DB99.DBD42" -> (99, 42), "DB99.DBB42" -> (99, 42).
    /// TR: "DB{n}.DB{X}{offset}" formatındaki adresten DB numarası ve byte offset çeker.
    ///     Örnek: "DB99.DBD42" -> (99, 42), "DB99.DBB42" -> (99, 42).
    /// </summary>
    private static (int db, int offset) ParseDbBlockAddress(string address)
    {
        var upper = address.ToUpperInvariant().Trim();
        var dotIdx = upper.IndexOf('.');
        if (dotIdx < 2 || !int.TryParse(upper[2..dotIdx], out int db))
            return (0, 0);
        // ".DB{tip_harfi}{offset}" › dotIdx+1="D", +2="B", +3=tip harfi, +4=offset başlıyor
        var rest = upper[(dotIdx + 4)..];
        var bitDot = rest.IndexOf('.');
        if (bitDot >= 0) rest = rest[..bitDot]; // bit adresi "42.5" › "42"
        int.TryParse(rest, out int offset);
        return (db, offset);
    }

    /// <summary>
    /// EN: Reads 12 bytes from the PLC for DTL (returned as a big-endian byte array).
    /// TR: DTL için PLC'den 12 byte okur (big-endian byte dizisi olarak döndürür).
    /// </summary>
    private async Task<byte[]> ReadDtlBytesAsync(string physicalAddress)
    {
        var (db, offset) = ParseDbBlockAddress(physicalAddress);
        return await _plc!.ReadBytesAsync(DataType.DataBlock, db, offset, 12);
    }

    /// <summary>
    /// EN: Writes a DTL value as 12 BCD/binary bytes to the PLC.
    /// TR: DTL değerini 12 byte BCD/binary olarak PLC'ye yazar.
    /// </summary>
    private async Task WriteDtlBytesAsync(string physicalAddress, object value)
    {
        var bytes = ConvertToDtlBytes(value);
        var (db, offset) = ParseDbBlockAddress(physicalAddress);
        await _plc!.WriteBytesAsync(DataType.DataBlock, db, offset, bytes);
    }

    /// <summary>
    /// EN: Converts a DTL byte[12] structure to "DTL#yyyy-MM-dd-HH:mm:ss.fff" format.
    ///     Structure: [0-1]=Year(UINT) [2]=Month [3]=Day [4]=DayOfWeek [5]=Hour [6]=Min [7]=Sec [8-11]=Nanoseconds(UDINT).
    /// TR: DTL byte[12] yapısını "DTL#yyyy-MM-dd-HH:mm:ss.fff" formatına dönüştürür.
    ///     Yapı: [0-1]=Yıl(UINT) [2]=Ay [3]=Gün [4]=HGünü [5]=Saat [6]=Dak [7]=San [8-11]=Nanosaniye(UDINT).
    /// </summary>
    private static string ConvertFromDtl(byte[] b)
    {
        if (b.Length < 12) return "DTL#(geçersiz)";
        int  year  = (b[0] << 8) | b[1];
        int  month = b[2];
        int  day   = b[3];
        // b[4] = haftanın günü (1=Pazar), atlanır
        int  hour  = b[5];
        int  min   = b[6];
        int  sec   = b[7];
        uint ns    = (uint)((b[8] << 24) | (b[9] << 16) | (b[10] << 8) | b[11]);
        int  ms    = (int)(ns / 1_000_000);
        try
        {
            var dt = new DateTime(year, Math.Max(1, month), Math.Max(1, day), hour, min, sec, Math.Min(999, ms));
            return $"DTL#{dt:yyyy-MM-dd-HH:mm:ss.fff}";
        }
        catch
        {
            return $"DTL#{year:D4}-{month:D2}-{day:D2}-{hour:D2}:{min:D2}:{sec:D2}.{ms:D3}";
        }
    }

    /// <summary>
    /// EN: Encodes a "DTL#yyyy-MM-dd-HH:mm:ss[.nnnnnnnnn]" string or DateTime value to an S7 DTL byte[12] structure.
    ///     Accepted formats: DTL#yyyy-MM-dd-HH:mm:ss, DTL#yyyy-MM-dd-HH:mm:ss.fff, DTL#yyyy-MM-dd HH:mm:ss, yyyy-MM-dd-HH:mm:ss.
    /// TR: "DTL#yyyy-MM-dd-HH:mm:ss[.nnnnnnnnn]" veya DateTime değerini S7 DTL byte[12] yapısına kodlar.
    ///     Kabul edilen formatlar: DTL#yyyy-MM-dd-HH:mm:ss, DTL#yyyy-MM-dd-HH:mm:ss.fff, DTL#yyyy-MM-dd HH:mm:ss, yyyy-MM-dd-HH:mm:ss.
    /// </summary>
    private static byte[] ConvertToDtlBytes(object value)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        DateTime dt;
        if (value is DateTime dtv)
        {
            dt = dtv;
        }
        else if (value is string s)
        {
            var clean = s.Trim();
            if (clean.StartsWith("DTL#", StringComparison.OrdinalIgnoreCase))
                clean = clean[4..];

            // Nanosaniye bölümünü ayır (varsa)
            uint extraNs = 0;
            var dotIdx = clean.LastIndexOf('.');
            // Nokta varsa ve zaman bölümünün içindeyse (son '-' veya ' ''den sonra)
            var lastSep = Math.Max(clean.LastIndexOf('-'), clean.LastIndexOf(' '));
            if (dotIdx > lastSep)
            {
                var nsPart = clean[(dotIdx + 1)..];
                if (nsPart.Length > 3 && uint.TryParse(nsPart.PadRight(9, '0')[..9], out var ns9))
                    extraNs = ns9;
                clean = clean[..dotIdx]; // nokta ve sonrasını at
            }

            // Son '-' ayracını boşluğa çevir (TIA Portal formatı: "yyyy-MM-dd-HH:mm:ss")
            var parts = clean.Split('-');
            if (parts.Length == 4)
                clean = $"{parts[0]}-{parts[1]}-{parts[2]} {parts[3]}";

            string[] formats =
            [
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd H:mm:ss",
                "yyyy-M-d HH:mm:ss",
                "yyyy-M-d H:mm:ss",
                "d.M.yyyy HH:mm:ss",
                "d.M.yyyy H:mm:ss"
            ];

            if (!DateTime.TryParseExact(clean, formats, ic,
                    System.Globalization.DateTimeStyles.None, out dt))
            {
                // Son çare: kültür bağımsız genel parse
                if (!DateTime.TryParse(clean, ic,
                        System.Globalization.DateTimeStyles.None, out dt))
                    throw new ArgumentException(
                        LocalizationManager.Instance.T("Ex_DtlInvalidDateFormat", s));
            }

            // Nanosaniye: nokta sonrası 3 haneden fazlaysa extraNs kullan, yoksa ms'den üret
            if (extraNs > 0)
            {
                // extraNs zaten tam 9 basamaklı nanosaniye değeri
                var b2 = new byte[12];
                int s7dow2 = (int)dt.DayOfWeek == 0 ? 1 : (int)dt.DayOfWeek + 1;
                b2[0]  = (byte)(dt.Year >> 8);
                b2[1]  = (byte)(dt.Year & 0xFF);
                b2[2]  = (byte)dt.Month;
                b2[3]  = (byte)dt.Day;
                b2[4]  = (byte)s7dow2;
                b2[5]  = (byte)dt.Hour;
                b2[6]  = (byte)dt.Minute;
                b2[7]  = (byte)dt.Second;
                b2[8]  = (byte)(extraNs >> 24);
                b2[9]  = (byte)((extraNs >> 16) & 0xFF);
                b2[10] = (byte)((extraNs >>  8) & 0xFF);
                b2[11] = (byte)(extraNs & 0xFF);
                return b2;
            }
        }
        else
        {
            try { dt = Convert.ToDateTime(value, ic); }
            catch { throw new ArgumentException(LocalizationManager.Instance.T("Ex_DtlConvertFailed", value)); }
        }

        int  s7dow = (int)dt.DayOfWeek == 0 ? 1 : (int)dt.DayOfWeek + 1;
        uint ns    = (uint)(dt.Millisecond * 1_000_000);
        var  b     = new byte[12];
        b[0]  = (byte)(dt.Year >> 8);
        b[1]  = (byte)(dt.Year & 0xFF);
        b[2]  = (byte)dt.Month;
        b[3]  = (byte)dt.Day;
        b[4]  = (byte)s7dow;
        b[5]  = (byte)dt.Hour;
        b[6]  = (byte)dt.Minute;
        b[7]  = (byte)dt.Second;
        b[8]  = (byte)(ns >> 24);
        b[9]  = (byte)((ns >> 16) & 0xFF);
        b[10] = (byte)((ns >>  8) & 0xFF);
        b[11] = (byte)(ns & 0xFF);
        return b;
    }

    /// <summary>
    /// EN: Reads the S7 string structure from the PLC for STRING[N].
    ///     Structure: byte[0]=maxLen, byte[1]=curLen, byte[2..]=Latin-1 characters.
    /// TR: STRING[N] için PLC'den S7 string yapısını okur.
    ///     Yapı: byte[0]=maxLen, byte[1]=curLen, byte[2..]=Latin-1 karakterler.
    /// </summary>
    private async Task<string> ReadStringAsync(string physicalAddress, string dataType)
    {
        var (db, offset) = ParseDbBlockAddress(physicalAddress);
        int maxLen = ParseStringLength(dataType);
        var bytes = await _plc!.ReadBytesAsync(DataType.DataBlock, db, offset, 2 + maxLen);
        int curLen = Math.Min(bytes[1], maxLen);
        return System.Text.Encoding.Latin1.GetString(bytes, 2, curLen);
    }

    /// <summary>
    /// EN: Reads the S7 wstring structure from the PLC for WSTRING[N].
    ///     Structure: ushort maxLen (BE), ushort curLen (BE), curLen×UTF-16BE characters.
    ///     Uses 2-step reading: first 4-byte header (to get real curLen), then only curLen×2 char bytes — prevents "Address out of range" errors.
    /// TR: WSTRING[N] için PLC'den S7 wstring yapısını okur.
    ///     Yapı: ushort maxLen (BE), ushort curLen (BE), curLen×UTF-16BE karakterler.
    ///     2 adımlı okuma: önce 4-byte header (gerçek curLen'i alır), sonra yalnızca curLen×2 char byte'ı okunur — "Address out of range" hatasını önler.
    /// </summary>
    private async Task<string> ReadWStringAsync(string physicalAddress, string dataType)
    {
        var (db, offset) = ParseDbBlockAddress(physicalAddress);
        // Adım 1: 4-byte header › maxLen ve curLen
        var header = await _plc!.ReadBytesAsync(DataType.DataBlock, db, offset, 4);
        int maxLen = (header[0] << 8) | header[1];
        int curLen = (header[2] << 8) | header[3];
        if (maxLen > 0) curLen = Math.Min(curLen, maxLen);
        if (curLen <= 0) return string.Empty;
        // Adım 2: yalnızca curLen×2 karakter byte'ını oku
        var charBytes = await _plc!.ReadBytesAsync(DataType.DataBlock, db, offset + 4, curLen * 2);
        var chars = new char[curLen];
        for (int i = 0; i < curLen; i++)
            chars[i] = (char)((charBytes[i * 2] << 8) | charBytes[i * 2 + 1]);
        return new string(chars);
    }

    /// <summary>
    /// EN: Writes a STRING[N] value to the PLC in S7 string format.
    ///     Structure: byte[0]=maxLen, byte[1]=curLen, byte[2..]=Latin-1 characters (remainder zeros).
    /// TR: STRING[N] değerini PLC'ye S7 string formatında yazar.
    ///     Yapı: byte[0]=maxLen, byte[1]=curLen, byte[2..]=Latin-1 karakterler (kalan sıfır).
    /// </summary>
    private async Task WriteStringAsync(string physicalAddress, string dataType, string value)
    {
        var (db, offset) = ParseDbBlockAddress(physicalAddress);
        int maxLen = ParseStringLength(dataType);
        if (value.Length > maxLen) value = value[..maxLen];
        var bytes = new byte[2 + maxLen];
        bytes[0] = (byte)maxLen;
        bytes[1] = (byte)value.Length;
        System.Text.Encoding.Latin1.GetBytes(value).CopyTo(bytes, 2);
        await _plc!.WriteBytesAsync(DataType.DataBlock, db, offset, bytes);
    }

    /// <summary>
    /// EN: Writes a WSTRING[N] value to the PLC in S7 wstring format.
    ///     Reads the real maxLen value from the PLC header — writes safely even if the length in the symbols file differs from the PLC.
    /// TR: WSTRING[N] değerini PLC'ye S7 wstring formatında yazar.
    ///     PLC'nin gerçek maxLen değerini header'dan okuyarak kullanır — sembol dosyasındaki uzunluk ile PLC'deki uzunluk farklı olsa dahi güvenli yazar.
    /// </summary>
    private async Task WriteWStringAsync(string physicalAddress, string dataType, string value)
    {
        var (db, offset) = ParseDbBlockAddress(physicalAddress);
        // PLC'nin gerçek maxLen'ini header'dan oku
        var header = await _plc!.ReadBytesAsync(DataType.DataBlock, db, offset, 4);
        int maxLen = (header[0] << 8) | header[1];
        // Eğer header geçersizse sembol dosyasındaki değeri kullan
        if (maxLen <= 0) maxLen = ParseStringLength(dataType);
        if (value.Length > maxLen) value = value[..maxLen];
        var bytes = new byte[4 + maxLen * 2];
        bytes[0] = (byte)(maxLen >> 8);
        bytes[1] = (byte)(maxLen & 0xFF);
        bytes[2] = (byte)(value.Length >> 8);
        bytes[3] = (byte)(value.Length & 0xFF);
        for (int i = 0; i < value.Length; i++)
        {
            bytes[4 + i * 2]     = (byte)(value[i] >> 8);
            bytes[4 + i * 2 + 1] = (byte)(value[i] & 0xFF);
        }
        await _plc!.WriteBytesAsync(DataType.DataBlock, db, offset, bytes);
    }

    /// <summary>
    /// EN: Extracts the length value from a type string in "STRING[N]" or "WSTRING[N]" format.
    ///     Example: "STRING[20]" -> 20, "WSTRING[100]" -> 100, "STRING" -> 254 (default).
    /// TR: "STRING[N]" veya "WSTRING[N]" formatındaki tip bilgisinden uzunluk değerini çeker.
    ///     Örnek: "STRING[20]" -> 20, "WSTRING[100]" -> 100, "STRING" -> 254 (varsayılan).
    /// </summary>
    private static int ParseStringLength(string dataType)
    {
        var start = dataType.IndexOf('[');
        var end   = dataType.IndexOf(']');
        if (start >= 0 && end > start + 1 &&
            int.TryParse(dataType[(start + 1)..end], out var len))
            return len;
        return 254;
    }
}