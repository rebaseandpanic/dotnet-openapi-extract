namespace PackageTypesContracts;

/// <summary>An item declared in a library of the application.</summary>
public class ContractItem
{
    /// <summary>Name of the item.</summary>
    public string Name { get; set; } = "";

    public int Undocumented { get; set; }
}
