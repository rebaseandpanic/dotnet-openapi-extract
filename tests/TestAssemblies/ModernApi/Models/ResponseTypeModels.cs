namespace ModernApi.Models.Responses;

/// <summary>Type named by a response attribute for a status code.</summary>
public class DeclaredBody
{
    /// <summary>Marker value.</summary>
    public string Declared { get; set; } = string.Empty;
}

/// <summary>Type named by <c>[Produces(typeof(T))]</c> on an action.</summary>
public class ProducedBody
{
    /// <summary>Marker value.</summary>
    public string Produced { get; set; } = string.Empty;
}

/// <summary>Type named by <c>[Produces(typeof(T))]</c> on a controller.</summary>
public class ControllerProducedBody
{
    /// <summary>Marker value.</summary>
    public string ControllerProduced { get; set; } = string.Empty;
}

/// <summary>Type named by a response attribute on a controller.</summary>
public class ControllerDeclaredBody
{
    /// <summary>Marker value.</summary>
    public string ControllerDeclared { get; set; } = string.Empty;
}

/// <summary>Return type of the action signature.</summary>
public class SignatureBody
{
    /// <summary>Marker value.</summary>
    public string Signature { get; set; } = string.Empty;
}

/// <summary>Error body declared on a controller.</summary>
public class ControllerError
{
    /// <summary>Error code.</summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>Error body declared on an action.</summary>
public class ActionError
{
    /// <summary>Error code.</summary>
    public string Reason { get; set; } = string.Empty;
}
