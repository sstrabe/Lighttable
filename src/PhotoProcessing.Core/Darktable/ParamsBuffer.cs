using System.Buffers.Binary;

namespace PhotoProcessing.Core.Darktable;

/// <summary>
/// A darktable module's params struct as raw little-endian bytes. darktable structs here are
/// all 4-byte fields (float, int, gboolean, enum), so offsets are simply field index * 4.
/// </summary>
public sealed class ParamsBuffer
{
    private readonly byte[] _bytes;

    public ParamsBuffer(int size) => _bytes = new byte[size];

    public ParamsBuffer(byte[] bytes) => _bytes = (byte[])bytes.Clone();

    public int Size => _bytes.Length;

    public byte[] ToArray() => (byte[])_bytes.Clone();

    public float GetFloat(int offset) => BinaryPrimitives.ReadSingleLittleEndian(_bytes.AsSpan(offset, 4));

    public void SetFloat(int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(_bytes.AsSpan(offset, 4), value);

    public int GetInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(offset, 4));

    public void SetInt(int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(_bytes.AsSpan(offset, 4), value);

    public void SetBool(int offset, bool value) => SetInt(offset, value ? 1 : 0);

    public ParamsBuffer Clone() => new(_bytes);
}
