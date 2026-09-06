using System.Buffers.Binary;
using RoboCamHub.Domain;

namespace RoboCamHub.Application;

public static class ImageAssetMetadata
{
    public static (uint Width, uint Height) ReadDimensions(string path, AssetMediaType mediaType)
    {
        using var stream = File.OpenRead(path);
        return mediaType switch
        {
            AssetMediaType.Png => ReadPng(stream),
            AssetMediaType.Jpeg => ReadJpeg(stream),
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
        };
    }

    private static (uint Width, uint Height) ReadPng(Stream stream)
    {
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!header[..8].SequenceEqual(signature) || !header[12..16].SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("The selected file is not a valid PNG image.");
        }
        var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        return width > 0 && height > 0
            ? (width, height)
            : throw new InvalidDataException("The PNG has invalid dimensions.");
    }

    private static (uint Width, uint Height) ReadJpeg(Stream stream)
    {
        if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
        {
            throw new InvalidDataException("The selected file is not a valid JPEG image.");
        }
        Span<byte> lengthBytes = stackalloc byte[2];
        Span<byte> dimensions = stackalloc byte[5];
        while (stream.Position < stream.Length)
        {
            if (stream.ReadByte() != 0xFF)
            {
                continue;
            }
            int marker;
            do
            {
                marker = stream.ReadByte();
            }
            while (marker == 0xFF);
            if (marker is -1 or 0xD9 or 0xDA)
            {
                break;
            }
            stream.ReadExactly(lengthBytes);
            var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
            if (length < 2)
            {
                throw new InvalidDataException("The JPEG contains an invalid segment.");
            }
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7
                or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                stream.ReadExactly(dimensions);
                var height = BinaryPrimitives.ReadUInt16BigEndian(dimensions[1..3]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(dimensions[3..5]);
                return width > 0 && height > 0
                    ? (width, height)
                    : throw new InvalidDataException("The JPEG has invalid dimensions.");
            }
            stream.Seek(length - 2, SeekOrigin.Current);
        }
        throw new InvalidDataException("The JPEG dimensions could not be read.");
    }
}
