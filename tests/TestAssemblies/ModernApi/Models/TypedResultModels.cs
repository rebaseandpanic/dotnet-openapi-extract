namespace ModernApi.Models.TypedResults;

/// <summary>Body of a typed result.</summary>
public class ResultItem
{
    /// <summary>Item name.</summary>
    public required string ItemName { get; set; }
}

/// <summary>Error body declared explicitly for a typed-result action.</summary>
public class ResultError
{
    /// <summary>Error reason.</summary>
    public required string Reason { get; set; }
}

/// <summary>Body type named by [Produces] on a typed-result action.</summary>
public class ProducedResultItem
{
    /// <summary>Produced marker.</summary>
    public required string ProducedMarker { get; set; }
}
