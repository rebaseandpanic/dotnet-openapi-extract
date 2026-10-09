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

/// <summary>Error body of a streaming action.</summary>
public class StreamError
{
    /// <summary>Error code.</summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>A parcel; a concrete polymorphic base used only as a streamed element.</summary>
[System.Text.Json.Serialization.JsonDerivedType(typeof(ExpressParcel), "express")]
public class Parcel
{
    /// <summary>Weight in grams.</summary>
    public int WeightGrams { get; set; }
}

/// <summary>An express parcel.</summary>
public sealed class ExpressParcel : Parcel
{
    /// <summary>Delivery deadline in hours.</summary>
    public int DeadlineHours { get; set; }
}
