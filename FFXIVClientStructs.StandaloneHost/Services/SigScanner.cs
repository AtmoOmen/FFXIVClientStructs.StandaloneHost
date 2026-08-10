using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Iced.Intel;

namespace FFXIVClientStructs.StandaloneHost.Services;

public sealed class SigScanner
{
    public SigScanner
    (
        ProcessModule module
    )
    {
        ArgumentNullException.ThrowIfNull(module);
        Module = module;

        using var stream = new FileStream(module.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new PEReader(stream);

        foreach (var section in reader.PEHeaders.SectionHeaders)
        {
            var size = section.VirtualSize > 0 ?
                           section.VirtualSize :
                           section.SizeOfRawData;

            switch (section.Name)
            {
                case ".text":
                    TextSectionOffset = section.VirtualAddress;
                    TextSectionSize   = size;
                    break;
                case ".data":
                    DataSectionOffset = section.VirtualAddress;
                    DataSectionSize   = size;
                    break;
                case ".rdata":
                    RDATASectionOffset = section.VirtualAddress;
                    RDATASectionSize   = size;
                    break;
            }
        }

        if (TextSectionSize == 0)
            throw new BadImageFormatException($"Module {module.ModuleName} does not contain a .text section.");
    }

    public ProcessModule Module { get; }

    public nint SearchBase => Module.BaseAddress;

    public nint TextSectionBase => SearchBase + TextSectionOffset;

    public int TextSectionOffset { get; }

    public int TextSectionSize { get; }

    public nint DataSectionBase => SearchBase + DataSectionOffset;

    public int DataSectionOffset { get; }

    public int DataSectionSize { get; }

    public nint RDATASectionBase => SearchBase + RDATASectionOffset;

    public int RDATASectionOffset { get; }

    public int RDATASectionSize { get; }

    public static nint Scan
    (
        nint   baseAddress,
        int    size,
        string signature
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        var pattern = ParseSignature(signature);
        var index   = IndexOf(baseAddress, size, pattern.Needle, pattern.Mask, pattern.BadShift);
        if (index < 0)
            throw new KeyNotFoundException($"Signature {signature} was not found.");

        return baseAddress + index;
    }

    public static bool TryScan
    (
        nint     baseAddress,
        int      size,
        string   signature,
        out nint result
    )
    {
        try
        {
            result = Scan(baseAddress, size, signature);
            return true;
        }
        catch (KeyNotFoundException)
        {
            result = 0;
            return false;
        }
    }

    public nint ScanModule
    (
        string signature
    ) => Scan(SearchBase, Module.ModuleMemorySize, signature);

    public bool TryScanModule
    (
        string   signature,
        out nint result
    ) => TryScan(SearchBase, Module.ModuleMemorySize, signature, out result);

    public nint ScanText
    (
        string signature
    )
    {
        var result = Scan(TextSectionBase, TextSectionSize, signature);
        var opcode = Marshal.ReadByte(result);

        if (opcode is 0xE8 or 0xE9)
        {
            result = result + 5 + Marshal.ReadInt32(result, 1);
            var textSectionEnd = TextSectionBase + TextSectionSize;
            if (result < TextSectionBase || result >= textSectionEnd)
                throw new KeyNotFoundException($"Signature {signature} resolved outside the .text section.");
        }

        return result;
    }

    public bool TryScanText
    (
        string   signature,
        out nint result
    )
    {
        try
        {
            result = ScanText(signature);
            return true;
        }
        catch (KeyNotFoundException)
        {
            result = 0;
            return false;
        }
    }

    public nint ScanData
    (
        string signature
    ) => Scan(DataSectionBase, DataSectionSize, signature);

    public bool TryScanData
    (
        string   signature,
        out nint result
    ) => TryScan(DataSectionBase, DataSectionSize, signature, out result);

    public nint ScanRDATA
    (
        string signature
    ) => Scan(RDATASectionBase, RDATASectionSize, signature);

    public bool TryScanRDATA
    (
        string   signature,
        out nint result
    ) => TryScan(RDATASectionBase, RDATASectionSize, signature, out result);

    public nint[] ScanAllText
    (
        string signature
    ) => ScanAllText(signature, CancellationToken.None).ToArray();

    public IEnumerable<nint> ScanAllText
    (
        string            signature,
        CancellationToken cancellationToken
    )
    {
        var pattern = ParseSignature(signature);
        var cursor  = TextSectionBase;
        var end     = TextSectionBase + TextSectionSize;

        while (cursor < end)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var index = IndexOf(cursor, (int)(end - cursor), pattern.Needle, pattern.Mask, pattern.BadShift);
            if (index < 0)
                yield break;

            var result = cursor + index;
            yield return result;
            cursor = result + 1;
        }
    }

    public nint GetStaticAddressFromSig
    (
        string signature,
        int    offset = 0
    )
    {
        var address = ScanText(signature) + offset;
        var end     = TextSectionBase     + TextSectionSize;
        if (address < TextSectionBase || address >= end)
            throw new KeyNotFoundException($"Signature {signature} resolved outside the .text section.");

        var length = Math.Min((int)(end - address), ParseSignature(signature).Needle.Length + 15);
        var bytes  = new byte[length];
        Marshal.Copy(address, bytes, 0, length);

        var reader  = new ByteArrayCodeReader(bytes);
        var decoder = Decoder.Create(64, reader, (ulong)address, DecoderOptions.AMD);

        while (reader.CanReadByte)
        {
            var instruction = decoder.Decode();
            if (instruction.IsInvalid)
                continue;

            if (instruction.Op0Kind is OpKind.Memory || instruction.Op1Kind is OpKind.Memory)
                return (nint)instruction.MemoryDisplacement64;
        }

        throw new KeyNotFoundException($"Signature {signature} does not reference a static address.");
    }

    public bool TryGetStaticAddressFromSig
    (
        string   signature,
        out nint result,
        int      offset = 0
    )
    {
        try
        {
            result = GetStaticAddressFromSig(signature, offset);
            return true;
        }
        catch (KeyNotFoundException)
        {
            result = 0;
            return false;
        }
    }

    public nint ResolveRelativeAddress
    (
        nint nextInstructionAddress,
        int  relativeOffset
    ) => nextInstructionAddress + relativeOffset;

    private static SignaturePattern ParseSignature
    (
        string signature
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);

        var compactSignature = string.Concat(signature.Where(character => !char.IsWhiteSpace(character)));
        if (compactSignature.Length == 0 || compactSignature.Length % 2 != 0)
            throw new ArgumentException("A signature must contain complete byte pairs.", nameof(signature));

        var needle = new byte[compactSignature.Length / 2];
        var mask   = new bool[needle.Length];

        for (var index = 0; index < needle.Length; index++)
        {
            var token = compactSignature.AsSpan(index * 2, 2);

            if (token is "??" or "**")
            {
                mask[index] = true;
                continue;
            }

            needle[index] = byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        return new SignaturePattern(needle, mask, BuildBadCharacterTable(needle, mask));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static unsafe int IndexOf
    (
        nint   bufferAddress,
        int    bufferLength,
        byte[] needle,
        bool[] mask,
        int[]  badShift
    )
    {
        if (needle.Length > bufferLength)
            return -1;

        var buffer    = (byte*)bufferAddress;
        var last      = needle.Length - 1;
        var offset    = 0;
        var maxOffset = bufferLength - needle.Length;

        while (offset <= maxOffset)
        {
            var position = last;

            while (needle[position] == buffer[position + offset] || mask[position])
            {
                if (position == 0)
                    return offset;

                position--;
            }

            offset += badShift[buffer[offset + last]];
        }

        return -1;
    }

    private static int[] BuildBadCharacterTable
    (
        byte[] needle,
        bool[] mask
    )
    {
        var last  = needle.Length - 1;
        var table = new int[256];

        if (last == 0)
        {
            Array.Fill(table, 1);
            return table;
        }

        var index = last;
        while (index > 0 && !mask[index])
            index--;

        var difference = Math.Max(1, last - index);
        Array.Fill(table, difference);

        for (index = last               - difference; index < last; index++)
            table[needle[index]] = last - index;

        return table;
    }

    private sealed record SignaturePattern
    (
        byte[] Needle,
        bool[] Mask,
        int[]  BadShift
    );
}
