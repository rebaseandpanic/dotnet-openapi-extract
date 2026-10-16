namespace LegacyNoNrtApi.Models;

/// <summary>An element component.</summary>
public class LegacyItem
{
    /// <summary>Name.</summary>
    public string Name { get; set; }
}

/// <summary>Collections, arrays and dictionaries of an assembly without nullable reference types.</summary>
public class LegacyCollectionsModel
{
    /// <summary>Strings.</summary>
    public List<string> Strings { get; set; }

    /// <summary>Item values by name.</summary>
    public Dictionary<string, LegacyItem> ItemsByName { get; set; }

    /// <summary>Lists of items.</summary>
    public List<List<LegacyItem>> NestedLists { get; set; }

    /// <summary>An array of items.</summary>
    public LegacyItem[] ItemArray { get; set; }
}
