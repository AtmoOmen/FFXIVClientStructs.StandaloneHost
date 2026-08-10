using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace FFXIVClientStructs.StandaloneHost;

internal static class PortableExecutable
{
    public static int GetExportRVA
    (
        string path,
        string exportName
    )
    {
        using var stream          = File.OpenRead(path);
        using var reader          = new PEReader(stream);
        var       headers         = reader.PEHeaders;
        var       peHeader        = headers.PEHeader ?? throw new StandaloneHostException($"{path} does not contain a PE header.");
        var       exportDirectory = peHeader.ExportTableDirectory;
        if (exportDirectory.RelativeVirtualAddress == 0 || exportDirectory.Size == 0)
            throw new StandaloneHostException($"{path} does not contain an export directory.");

        var image           = reader.GetEntireImage().GetContent().AsSpan();
        var directoryOffset = RVAToOffset(headers, exportDirectory.RelativeVirtualAddress);
        EnsureAvailable(image, directoryOffset, 40);

        var nameCount       = ReadInt32(image, directoryOffset + 24);
        var functionsRVA    = ReadInt32(image, directoryOffset + 28);
        var namesRVA        = ReadInt32(image, directoryOffset + 32);
        var ordinalsRVA     = ReadInt32(image, directoryOffset + 36);
        var functionsOffset = RVAToOffset(headers, functionsRVA);
        var namesOffset     = RVAToOffset(headers, namesRVA);
        var ordinalsOffset  = RVAToOffset(headers, ordinalsRVA);

        for (var index = 0; index < nameCount; index++)
        {
            var nameRVA = ReadInt32(image, namesOffset + (index * sizeof(int)));
            var name    = ReadNullTerminatedASCII(image, RVAToOffset(headers, nameRVA));
            if (!string.Equals(name, exportName, StringComparison.Ordinal))
                continue;

            var ordinal     = ReadUInt16(image, ordinalsOffset + (index   * sizeof(ushort)));
            var functionRVA = ReadInt32(image, functionsOffset + (ordinal * sizeof(int)));
            var exportEnd   = exportDirectory.RelativeVirtualAddress + exportDirectory.Size;
            if (functionRVA >= exportDirectory.RelativeVirtualAddress && functionRVA < exportEnd)
                throw new StandaloneHostException($"Export {exportName} is forwarded.");

            return functionRVA;
        }

        throw new StandaloneHostException($"Export {exportName} was not found in {path}.");
    }

    private static int RVAToOffset
    (
        PEHeaders headers,
        int       rva
    )
    {
        var peHeader = headers.PEHeader ?? throw new StandaloneHostException("The PE header is unavailable.");
        if (rva < peHeader.SizeOfHeaders)
            return rva;

        foreach (var section in headers.SectionHeaders)
        {
            var sectionSize = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + sectionSize)
                return section.PointerToRawData                               + rva - section.VirtualAddress;
        }

        throw new StandaloneHostException($"RVA 0x{rva:X} does not map to a PE section.");
    }

    private static int ReadInt32
    (
        ReadOnlySpan<byte> image,
        int                offset
    )
    {
        EnsureAvailable(image, offset, sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(image[offset..]);
    }

    private static ushort ReadUInt16
    (
        ReadOnlySpan<byte> image,
        int                offset
    )
    {
        EnsureAvailable(image, offset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(image[offset..]);
    }

    private static string ReadNullTerminatedASCII
    (
        ReadOnlySpan<byte> image,
        int                offset
    )
    {
        EnsureAvailable(image, offset, 1);
        var terminator = image[offset..].IndexOf((byte)0);
        if (terminator < 0)
            throw new StandaloneHostException("The PE export name is not null terminated.");

        return Encoding.ASCII.GetString(image.Slice(offset, terminator));
    }

    private static void EnsureAvailable
    (
        ReadOnlySpan<byte> image,
        int                offset,
        int                length
    )
    {
        if (offset < 0 || length < 0 || offset > image.Length - length)
            throw new StandaloneHostException("The PE export directory is truncated.");
    }
}
