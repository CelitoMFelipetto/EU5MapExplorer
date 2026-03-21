using System.IO.Compression;
using System.Text;

namespace EU5MapExplorer.Api;

public static class Eu5SaveInspector
{
    public static string ReadHeaderLine(string fullPath)
    {
        using var fs = File.OpenRead(fullPath);
        Span<byte> buffer = stackalloc byte[4096];

        var bytes = new List<byte>(128);
        while (true)
        {
            var read = fs.Read(buffer);
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                if (b == (byte)'\n')
                {
                    var line = Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
                    return line;
                }

                bytes.Add(b);
                if (bytes.Count > 512)
                {
                    throw new InvalidOperationException("Header line too long.");
                }
            }
        }

        if (bytes.Count == 0)
        {
            throw new InvalidOperationException("File was empty.");
        }

        return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
    }

    public static Eu5SaveHeader ParseHeader(string headerLine)
    {
        // Wiki reference: https://eu5.paradoxwikis.com/Save-game_editing
        if (headerLine.Length < 31 || !headerLine.StartsWith("SAV", StringComparison.Ordinal))
        {
            return new Eu5SaveHeader(
                Raw: headerLine,
                IsRecognized: false,
                Version: null,
                SaveTypeCode: null,
                SaveType: null,
                FormatCode: null,
                Format: null,
                Segment1: null,
                Segment2: null,
                Trailing: null);
        }

        string? version = headerLine.Substring(3, 2);
        string? typeCode = headerLine.Substring(5, 2);
        string? segment1 = headerLine.Substring(7, 8);
        string? formatCode = headerLine.Substring(15, 4);
        string? segment2 = headerLine.Substring(19, 4);
        string? trailing = headerLine.Length >= 31 ? headerLine.Substring(23, 8) : null;

        string? type = typeCode switch
        {
            "00" => "Debug (text) save",
            "03" => "Packed save",
            _ => "Unknown"
        };

        string? format = formatCode switch
        {
            "0004" => "Text",
            "0006" => "Packed",
            _ => "Unknown"
        };

        return new Eu5SaveHeader(
            Raw: headerLine,
            IsRecognized: true,
            Version: version,
            SaveTypeCode: typeCode,
            SaveType: type,
            FormatCode: formatCode,
            Format: format,
            Segment1: segment1,
            Segment2: segment2,
            Trailing: trailing);
    }

    public static Eu5Inspection Inspect(
        string fullPath,
        int afterHeaderPreviewBytes,
        bool includeZipEntryPreview,
        bool includeGamestateTree,
        int maxGamestateNodes,
        int maxGamestateDepth)
    {
        var afterHeader = ReadAfterHeaderPreview(fullPath, afterHeaderPreviewBytes);
        var looksText = LooksLikeMostlyText(afterHeader);

        var zipOffset = FindZipOffset(fullPath, 0);
        Eu5EmbeddedZipInfo? zip = null;
        Eu5GamestateTree? gamestate = null;

        if (zipOffset is not null)
        {
            try
            {
                using var fs = File.OpenRead(fullPath);
                using var zipStream = new OffsetStream(fs, zipOffset.Value);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

                var stringLookup = ReadStringLookup(archive);

                var entries = archive.Entries.Select(e =>
                {
                    string? entryHexPreview = null;
                    if (includeZipEntryPreview && e.Length > 0 && e.Length <= 1024 * 1024)
                    {
                        using var entryStream = e.Open();
                        var buf = new byte[Math.Min(256, (int)Math.Min(e.Length, int.MaxValue))];
                        var r = entryStream.Read(buf, 0, buf.Length);
                        entryHexPreview = ToHexPreview(r == buf.Length ? buf : buf.AsSpan(0, r).ToArray(), 64);
                    }

                    return new Eu5ZipEntryInfo(
                        FullName: e.FullName,
                        Length: e.Length,
                        CompressedLength: e.CompressedLength,
                        LastWriteTime: e.LastWriteTime,
                        HexPreview: entryHexPreview);
                }).ToArray();

                zip = new Eu5EmbeddedZipInfo(
                    Offset: zipOffset.Value,
                    EntryCount: entries.Length,
                    Entries: entries,
                    Error: null,
                    Exception: null,
                    Message: null);

                if (includeGamestateTree)
                {
                    gamestate = ParseGamestateTree(archive, stringLookup, maxGamestateNodes, maxGamestateDepth);
                }
            }
            catch (Exception ex)
            {
                zip = new Eu5EmbeddedZipInfo(
                    Offset: zipOffset.Value,
                    EntryCount: null,
                    Entries: null,
                    Error: "Failed to read embedded zip.",
                    Exception: ex.GetType().Name,
                    Message: ex.Message);

                gamestate = null;
            }
        }

        return new Eu5Inspection(
            AfterHeader: new Eu5AfterHeaderInfo(
                PreviewBytes: afterHeader.Length,
                LooksMostlyText: looksText,
                HexPreview: ToHexPreview(afterHeader, 64)),
            EmbeddedZip: zip,
            Gamestate: gamestate);
    }

    private static IReadOnlyList<string>? ReadStringLookup(ZipArchive archive)
    {
        var entry = archive.GetEntry("string_lookup");
        if (entry is null) return null;

        using var s = entry.Open();
        // header: 1 byte unknown, 2 byte count (LE), 2 byte unknown
        var header = new byte[5];
        var r = s.Read(header, 0, header.Length);
        if (r != header.Length) return null;

        var count = header[1] | (header[2] << 8);
        var strings = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var lenLo = s.ReadByte();
            var lenHi = s.ReadByte();
            if (lenLo < 0 || lenHi < 0) break;
            var len = lenLo | (lenHi << 8);
            if (len < 0) break;

            var buf = new byte[len];
            var read = 0;
            while (read < len)
            {
                var n = s.Read(buf, read, len - read);
                if (n <= 0) break;
                read += n;
            }

            if (read != len) break;
            strings.Add(Encoding.UTF8.GetString(buf));
        }

        return strings;
    }

    private static Eu5GamestateTree ParseGamestateTree(ZipArchive archive, IReadOnlyList<string>? stringLookup, int maxNodes, int maxDepth)
    {
        var entry = archive.GetEntry("gamestate") ?? throw new InvalidOperationException("gamestate entry not found");
        using var s = entry.Open();
        var r = new LeReader(s);

        var budget = new NodeBudget(maxNodes);
        var root = ReadObject(r, stringLookup, budget, depth: 0, maxDepth: maxDepth, captureChildren: true);
        return new Eu5GamestateTree(root, budget.MaxNodes, budget.Consumed, budget.Truncated, maxDepth);
    }

    private static Eu5Node ReadObject(
        LeReader r,
        IReadOnlyList<string>? stringLookup,
        NodeBudget budget,
        int depth,
        int maxDepth,
        bool captureChildren)
    {
        // Object is a sequence of entries until 0x0004 terminator (as per wiki).
        List<Eu5Node>? children = captureChildren ? new List<Eu5Node>() : null;

        while (true)
        {
            if (!r.TryReadUInt16(out var token))
            {
                budget.Truncated = true;
                break;
            }

            if (token == 0x0004)
            {
                break;
            }

            // Peek for 0x0001 marker: if present, token is a key; otherwise token represents a value in an array-like list.
            if (r.TryPeekUInt16(out var marker) && marker == 0x0001)
            {
                r.ReadUInt16(); // consume marker
                var key = ResolveName(token, r, stringLookup);

                if (!r.TryReadUInt16(out var valueType))
                {
                    if (children is not null)
                    {
                        children.Add(budget.Consume(new Eu5Node(key, "unknown", null, null, true)));
                    }
                    break;
                }

                var shouldCaptureValue = captureChildren && depth < maxDepth;
                var valueNode = ReadValue(
                    r,
                    stringLookup,
                    budget,
                    valueType,
                    depth: depth,
                    maxDepth: maxDepth,
                    capture: shouldCaptureValue);

                if (children is not null && valueNode is not null)
                {
                    children.Add(budget.Consume(valueNode with { Key = key }));
                }
            }
            else
            {
                // Array element value-type token.
                var shouldCaptureValue = captureChildren && depth < maxDepth;
                var valueNode = ReadValue(
                    r,
                    stringLookup,
                    budget,
                    token,
                    depth: depth,
                    maxDepth: maxDepth,
                    capture: shouldCaptureValue);

                if (children is not null && valueNode is not null)
                {
                    children.Add(budget.Consume(valueNode));
                }
            }
        }

        var truncated = budget.Truncated || (captureChildren && depth >= maxDepth);
        var node = new Eu5Node(Key: null, Kind: "object", Value: null, Children: children, Truncated: truncated);
        return budget.Consume(node);
    }

    private static Eu5Node? ReadValue(
        LeReader r,
        IReadOnlyList<string>? stringLookup,
        NodeBudget budget,
        ushort valueType,
        int depth,
        int maxDepth,
        bool capture)
    {
        // Minimal set based on wiki types.
        switch (valueType)
        {
            case 0x0003:
            {
                var nextDepth = depth + 1;
                // If we're beyond depth, we still must parse/skip the subobject for alignment,
                // but we won't keep its children in the returned JSON.
                var child = ReadObject(
                    r,
                    stringLookup,
                    budget,
                    depth: nextDepth,
                    maxDepth: maxDepth,
                    captureChildren: capture && nextDepth <= maxDepth);

                if (!capture)
                {
                    return null;
                }

                return child with { Kind = "subobject", Truncated = child.Truncated || nextDepth > maxDepth };
            }
            case 0x000c:
            case 0x0014:
            case 0x029c:
            {
                if (!r.TryReadInt32(out var i32))
                {
                    budget.Truncated = true;
                    return capture ? new Eu5Node(null, $"int32(0x{valueType:x4})", null, null, true) : null;
                }
                return capture ? new Eu5Node(null, $"int32(0x{valueType:x4})", i32, null, false) : null;
            }
            case 0x000e:
            {
                if (!r.TryReadByte(out var b))
                {
                    budget.Truncated = true;
                    return capture ? new Eu5Node(null, "bool", null, null, true) : null;
                }
                return capture ? new Eu5Node(null, "bool", b != 0, null, false) : null;
            }
            case 0x000f:
            case 0x0017:
            {
                // Hollerith string: 16-bit length + bytes
                if (!r.TryReadUInt16(out var len))
                {
                    budget.Truncated = true;
                    return capture ? new Eu5Node(null, "string", null, null, true) : null;
                }
                if (!r.TryReadBytes(len, out var bytes))
                {
                    budget.Truncated = true;
                    return capture ? new Eu5Node(null, "string", null, null, true) : null;
                }
                var str = Encoding.UTF8.GetString(bytes);
                return capture ? new Eu5Node(null, "string", str, null, false) : null;
            }
            case 0x0167:
            {
                // 64-bit fixed-point integer: value / 10000.0
                if (!r.TryReadInt64(out var raw))
                {
                    budget.Truncated = true;
                    return capture ? new Eu5Node(null, "fixed64", null, null, true) : null;
                }
                var value = raw / 10000.0;
                return capture ? new Eu5Node(null, "fixed64", value, null, false) : null;
            }
            case 0x0d40:
            {
                // 8-bit index into string_lookup
                if (!r.TryReadByte(out var idx))
                {
                    budget.Truncated = true;
                    return capture ? new Eu5Node(null, "string_ref8", null, null, true) : null;
                }
                var name = idx < (stringLookup?.Count ?? 0) ? stringLookup![idx] : $"<string_lookup[{idx}]>";
                return capture ? new Eu5Node(null, "string_ref8", name, null, false) : null;
            }
            case 0x0d3e:
            {
                // 16-bit index into string_lookup
                if (!r.TryReadUInt16(out var idx16))
                {
                    budget.Truncated = true;
                    return capture ? new Eu5Node(null, "string_ref16", null, null, true) : null;
                }
                var name = idx16 < (stringLookup?.Count ?? 0) ? stringLookup![(int)idx16] : $"<string_lookup[{idx16}]>";
                return capture ? new Eu5Node(null, "string_ref16", name, null, false) : null;
            }
            default:
            {
                // Unknown value type: keep a small hex preview to keep parsing aligned? We can't.
                // Best effort: represent unknown type and stop to avoid desync.
                budget.Truncated = true;
                return capture ? new Eu5Node(null, $"unknown_value_type(0x{valueType:x4})", null, null, true) : null;
            }
        }
    }

    private static string ResolveName(ushort nameToken, LeReader r, IReadOnlyList<string>? stringLookup)
    {
        // Name token can be a string-table lookup directive (0x0d40 or 0x0d3e) or just an ID.
        if (nameToken == 0x0d40)
        {
            r.TryReadByte(out var idx);
            return idx < (stringLookup?.Count ?? 0) ? stringLookup![idx] : $"<string_lookup[{idx}]>";
        }
        if (nameToken == 0x0d3e)
        {
            r.TryReadUInt16(out var idx);
            return idx < (stringLookup?.Count ?? 0) ? stringLookup![(int)idx] : $"<string_lookup[{idx}]>";
        }

        return $"0x{nameToken:x4}";
    }

    private static byte[] ReadAfterHeaderPreview(string fullPath, int maxBytes)
    {
        using var fs = File.OpenRead(fullPath);
        int b;
        while ((b = fs.ReadByte()) != -1)
        {
            if (b == '\n')
            {
                break;
            }
        }

        if (b == -1)
        {
            return Array.Empty<byte>();
        }

        var toRead = Math.Clamp(maxBytes, 0, 1024 * 1024);
        var buf = new byte[toRead];
        var read = fs.Read(buf, 0, toRead);
        return read == buf.Length ? buf : buf.AsSpan(0, read).ToArray();
    }

    private static long? FindZipOffset(string fullPath, long startOffset)
    {
        using var fs = File.OpenRead(fullPath);
        if (startOffset > 0)
        {
            fs.Seek(startOffset, SeekOrigin.Begin);
        }

        const int chunkSize = 64 * 1024;
        var buffer = new byte[chunkSize];
        var overlap = Array.Empty<byte>();
        long absolute = startOffset;

        while (true)
        {
            var read = fs.Read(buffer, 0, buffer.Length);
            if (read <= 0) return null;

            ReadOnlySpan<byte> span = buffer.AsSpan(0, read);
            if (overlap.Length > 0)
            {
                var combined = new byte[overlap.Length + read];
                Buffer.BlockCopy(overlap, 0, combined, 0, overlap.Length);
                Buffer.BlockCopy(buffer, 0, combined, overlap.Length, read);
                span = combined;
                absolute -= overlap.Length;
            }

            for (var i = 0; i <= span.Length - 4; i++)
            {
                if (span[i] == (byte)'P' && span[i + 1] == (byte)'K' && span[i + 2] == 0x03 && span[i + 3] == 0x04)
                {
                    return absolute + i;
                }
            }

            overlap = span.Length >= 3 ? span.Slice(span.Length - 3, 3).ToArray() : span.ToArray();
            absolute += span.Length;
        }
    }

    private static string ToHexPreview(byte[] bytes, int max)
    {
        var take = Math.Clamp(max, 0, bytes.Length);
        if (take == 0) return "";
        return Convert.ToHexString(bytes.AsSpan(0, take)).ToLowerInvariant();
    }

    private static bool LooksLikeMostlyText(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        var texty = 0;
        foreach (var b in bytes)
        {
            if (b == 9 || b == 10 || b == 13 || (b >= 32 && b <= 126))
            {
                texty++;
            }
        }
        return (double)texty / bytes.Length >= 0.95;
    }

    private sealed class OffsetStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _start;
        private long _position;

        public OffsetStream(Stream inner, long start)
        {
            _inner = inner;
            _start = start;
            _position = 0;
            _inner.Seek(_start, SeekOrigin.Begin);
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length - _start;
        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
            };

            if (target < 0) throw new IOException("Cannot seek before start.");
            _inner.Seek(_start + target, SeekOrigin.Begin);
            _position = target;
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

public record Eu5SaveHeader(
    string Raw,
    bool IsRecognized,
    string? Version,
    string? SaveTypeCode,
    string? SaveType,
    string? FormatCode,
    string? Format,
    string? Segment1,
    string? Segment2,
    string? Trailing);

public record Eu5Inspection(Eu5AfterHeaderInfo AfterHeader, Eu5EmbeddedZipInfo? EmbeddedZip, Eu5GamestateTree? Gamestate);

public record Eu5AfterHeaderInfo(int PreviewBytes, bool LooksMostlyText, string HexPreview);

public record Eu5ZipEntryInfo(
    string FullName,
    long Length,
    long CompressedLength,
    DateTimeOffset LastWriteTime,
    string? HexPreview);

public record Eu5EmbeddedZipInfo(
    long Offset,
    int? EntryCount,
    Eu5ZipEntryInfo[]? Entries,
    string? Error,
    string? Exception,
    string? Message);

public record Eu5GamestateTree(Eu5Node Root, int MaxNodes, int NodesProduced, bool Truncated, int MaxDepth);

public record Eu5Node(
    string? Key,
    string Kind,
    object? Value,
    IReadOnlyList<Eu5Node>? Children,
    bool Truncated);

internal sealed class NodeBudget
{
    public int MaxNodes { get; }
    public int Consumed { get; private set; }
    public bool Truncated { get; set; }
    public bool ShouldStop => Consumed >= MaxNodes;

    public NodeBudget(int maxNodes)
    {
        MaxNodes = maxNodes;
    }

    public Eu5Node Consume(Eu5Node node)
    {
        if (Consumed < MaxNodes)
        {
            Consumed++;
            return node;
        }

        Truncated = true;
        return node with { Truncated = true };
    }
}

internal sealed class LeReader
{
    private readonly Stream _s;
    private readonly byte[] _buf = new byte[8192];
    private int _pos;
    private int _len;

    public LeReader(Stream s)
    {
        _s = s;
    }

    private bool Ensure(int count)
    {
        if (_len - _pos >= count) return true;

        if (_pos > 0 && _pos < _len)
        {
            Buffer.BlockCopy(_buf, _pos, _buf, 0, _len - _pos);
            _len -= _pos;
            _pos = 0;
        }
        else if (_pos >= _len)
        {
            _pos = 0;
            _len = 0;
        }

        while (_len < count)
        {
            var read = _s.Read(_buf, _len, _buf.Length - _len);
            if (read <= 0) return false;
            _len += read;
        }
        return true;
    }

    public bool TryPeekUInt16(out ushort value)
    {
        value = 0;
        if (!Ensure(2)) return false;
        value = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8));
        return true;
    }

    public bool TryReadByte(out byte value)
    {
        value = 0;
        if (!Ensure(1)) return false;
        value = _buf[_pos++];
        return true;
    }

    public ushort ReadUInt16()
    {
        if (!Ensure(2)) throw new EndOfStreamException();
        var v = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8));
        _pos += 2;
        return v;
    }

    public bool TryReadUInt16(out ushort value)
    {
        value = 0;
        if (!Ensure(2)) return false;
        value = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8));
        _pos += 2;
        return true;
    }

    public bool TryReadInt32(out int value)
    {
        value = 0;
        if (!Ensure(4)) return false;
        value = _buf[_pos]
            | (_buf[_pos + 1] << 8)
            | (_buf[_pos + 2] << 16)
            | (_buf[_pos + 3] << 24);
        _pos += 4;
        return true;
    }

    public bool TryReadInt64(out long value)
    {
        value = 0;
        if (!Ensure(8)) return false;
        value = (long)_buf[_pos]
            | ((long)_buf[_pos + 1] << 8)
            | ((long)_buf[_pos + 2] << 16)
            | ((long)_buf[_pos + 3] << 24)
            | ((long)_buf[_pos + 4] << 32)
            | ((long)_buf[_pos + 5] << 40)
            | ((long)_buf[_pos + 6] << 48)
            | ((long)_buf[_pos + 7] << 56);
        _pos += 8;
        return true;
    }

    public bool TryReadBytes(int count, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (count < 0) return false;
        if (!Ensure(count)) return false;
        bytes = new byte[count];
        Buffer.BlockCopy(_buf, _pos, bytes, 0, count);
        _pos += count;
        return true;
    }
}

