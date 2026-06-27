using System.Buffers;

namespace AdbSharp.Adb.Internal;

internal sealed class PooledMemoryStream : Stream
{
    private const int DefaultCapacity = 256;
    private byte[] buffer = ArrayPool<byte>.Shared.Rent(DefaultCapacity);
    private int length;
    private int position;
    private bool disposed;

    public override bool CanRead => !disposed;

    public override bool CanSeek => !disposed;

    public override bool CanWrite => !disposed;

    public override long Length
    {
        get
        {
            ThrowIfDisposed();
            return length;
        }
    }

    public override long Position
    {
        get
        {
            ThrowIfDisposed();
            return position;
        }

        set
        {
            ThrowIfDisposed();
            if (value is < 0 or > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Stream position must fit in a non-negative Int32 value.");
            }

            position = checked((int)value);
        }
    }

    public byte[] GetBuffer()
    {
        ThrowIfDisposed();
        return buffer;
    }

    public override void Flush()
    {
        ThrowIfDisposed();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("The offset and count exceed the destination buffer length.", nameof(count));
        }

        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> destination)
    {
        ThrowIfDisposed();
        var count = Math.Min(destination.Length, length - position);
        if (count <= 0)
        {
            return 0;
        }

        this.buffer.AsSpan(position, count).CopyTo(destination);
        position += count;
        return count;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), "Unknown seek origin.")
        };

        if (target is < 0 or > int.MaxValue)
        {
            throw new IOException("Cannot seek outside the supported pooled stream range.");
        }

        position = checked((int)target);
        return position;
    }

    public override void SetLength(long value)
    {
        ThrowIfDisposed();
        if (value is < 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Stream length must fit in a non-negative Int32 value.");
        }

        var newLength = checked((int)value);
        EnsureCapacity(newLength);
        if (newLength > length)
        {
            buffer.AsSpan(length, newLength - length).Clear();
        }

        length = newLength;
        if (position > length)
        {
            position = length;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("The offset and count exceed the source buffer length.", nameof(count));
        }

        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> source)
    {
        ThrowIfDisposed();
        var end = checked(position + source.Length);
        EnsureCapacity(end);
        if (position > length)
        {
            buffer.AsSpan(length, position - length).Clear();
        }

        source.CopyTo(buffer.AsSpan(position));
        position = end;
        length = Math.Max(length, position);
    }

    public byte[] ToArray()
    {
        ThrowIfDisposed();
        var result = new byte[length];
        buffer.AsSpan(0, length).CopyTo(result);
        return result;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposed)
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            buffer = [];
            length = 0;
            position = 0;
            disposed = true;
        }

        base.Dispose(disposing);
    }

    private void EnsureCapacity(int minimum)
    {
        if (buffer.Length >= minimum)
        {
            return;
        }

        var doubled = buffer.Length <= int.MaxValue / 2 ? buffer.Length * 2 : int.MaxValue;
        var newCapacity = Math.Max(minimum, doubled);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
        buffer.AsSpan(0, length).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        buffer = newBuffer;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
