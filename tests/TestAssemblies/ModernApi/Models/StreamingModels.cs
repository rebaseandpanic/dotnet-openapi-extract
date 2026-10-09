namespace ModernApi.Models.Streaming;

/// <summary>One element of a streamed response.</summary>
public class StreamItem
{
    /// <summary>Position of the element in the stream.</summary>
    public required int Sequence { get; set; }

    /// <summary>Payload text.</summary>
    public required string Text { get; set; }
}

/// <summary>A user type that is an asynchronous sequence of <see cref="StreamItem"/>.</summary>
public sealed class StreamItemSequence(IReadOnlyList<StreamItem> items) : IAsyncEnumerable<StreamItem>
{
    /// <inheritdoc />
    public async IAsyncEnumerator<StreamItem> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
