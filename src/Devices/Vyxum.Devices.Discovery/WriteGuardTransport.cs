namespace Vyxum.Devices.Discovery;

/// <summary>
/// Wraps a transport so that only whitelisted byte sequences can be written; any other write throws
/// <see cref="InvalidOperationException"/> before a byte reaches the device. Also counts the bytes read.
/// </summary>
public sealed class WriteGuardTransport : ITransport
{
    private readonly ITransport _inner;
    private readonly IReadOnlyList<byte[]> _allowed;
    private long _bytesRead;

    /// <summary>Creates the guard.</summary>
    /// <param name="inner">The transport to protect.</param>
    /// <param name="allowedWrites">Every byte sequence that may be written, each as one whole write.</param>
    public WriteGuardTransport(ITransport inner, IReadOnlyList<byte[]> allowedWrites)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(allowedWrites);
        _inner = inner;
        _allowed = [.. allowedWrites.Select(a => (byte[])a.Clone())];
    }

    /// <inheritdoc />
    public string Description => _inner.Description;

    /// <summary>True after a write to the inner transport failed (for example a write timeout).</summary>
    public bool WriteFailed { get; private set; }

    /// <summary>The number of bytes read so far.</summary>
    public long BytesRead => Interlocked.Read(ref _bytesRead);

    /// <summary>True when <paramref name="buffer"/> is exactly one of the allowed sequences.</summary>
    public bool IsAllowed(ReadOnlySpan<byte> buffer)
    {
        foreach (var allowed in _allowed)
        {
            if (buffer.SequenceEqual(allowed))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

    /// <inheritdoc />
    public Task CloseAsync() => _inner.CloseAsync();

    /// <inheritdoc />
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!IsAllowed(buffer.Span))
        {
            throw new InvalidOperationException(
                $"Device discovery tried to write {buffer.Length} byte(s) that are not on the read-only whitelist; nothing was sent.");
        }

        return WriteCoreAsync(buffer, cancellationToken);
    }

    private async ValueTask WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WriteFailed = true;
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var count = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Interlocked.Add(ref _bytesRead, count);
        return count;
    }

    /// <inheritdoc />
    public void DiscardInput() => _inner.DiscardInput();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
