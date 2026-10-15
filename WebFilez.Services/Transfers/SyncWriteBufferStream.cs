namespace WebFilez.Services.Transfers;

/// <summary>
/// A write-only stream that holds synchronous writes in memory until the next asynchronous call.
/// <see cref="System.IO.Compression.ZipArchive"/> still writes synchronously when an entry is
/// closed, even through its async API, and ASP.NET Core response bodies reject synchronous I/O.
/// Those writes are only the tail of each entry, so the buffer stays small as long as
/// <see cref="FlushAsync(CancellationToken)"/> is called between entries.
/// </summary>
internal sealed class SyncWriteBufferStream(Stream inner) : Stream
{
    private readonly MemoryStream pending = new();
    private long position;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => position;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        pending.Write(buffer);
        position += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await DrainAsync(cancellationToken);
        await inner.WriteAsync(buffer, cancellationToken);
        position += buffer.Length;
    }

    // The zip writer flushes synchronously too; the bytes go out on the next async call.
    public override void Flush()
    {
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await DrainAsync(cancellationToken);
        await inner.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        await DrainAsync(CancellationToken.None);
        await pending.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        if (pending.Length == 0)
        {
            return;
        }

        pending.Position = 0;
        await pending.CopyToAsync(inner, cancellationToken);
        pending.SetLength(0);
    }
}