namespace ModernApi.Models.Contexts;

/// <summary>Order summary whose multi-word property names differ under every naming policy.</summary>
public class OrderSummary
{
    /// <summary>Human-readable order number.</summary>
    public required string OrderNumber { get; set; }

    /// <summary>Number of items in the order.</summary>
    public required int ItemCount { get; set; }

    /// <summary>Optional note left by the customer.</summary>
    public string? CustomerNote { get; set; }
}

/// <summary>A real type whose name is the HTTP-context id of <see cref="OrderSummary"/>.</summary>
public class OrderSummaryHttp
{
    /// <summary>Marker that tells this component from the HTTP schema of the order summary.</summary>
    public required string RealDtoMarker { get; set; }
}

/// <summary>A generic envelope used in both contexts.</summary>
public class Envelope<T>
{
    /// <summary>The wrapped payload.</summary>
    public required T Payload { get; set; }

    /// <summary>When the envelope was sealed.</summary>
    public DateTimeOffset SealedAt { get; set; }
}

/// <summary>A real type whose name is the HTTP-context id of the envelope of an order summary.</summary>
public class OrderSummaryEnvelopeHttp
{
    /// <summary>Marker that tells this component from the HTTP schema of the envelope.</summary>
    public required string RealEnvelopeMarker { get; set; }
}

/// <summary>A shipment; a concrete polymorphic base used in both contexts.</summary>
[System.Text.Json.Serialization.JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[System.Text.Json.Serialization.JsonDerivedType(typeof(ExpressShipment), "express")]
public class Shipment
{
    /// <summary>Tracking code of the shipment.</summary>
    public required string TrackingCode { get; set; }
}

/// <summary>An express shipment.</summary>
public sealed class ExpressShipment : Shipment
{
    /// <summary>Promised delivery window in hours.</summary>
    public required int DeliveryWindowHours { get; set; }
}

/// <summary>A real type whose name is the variant id of an express shipment.</summary>
public class ExpressShipmentAsShipment
{
    /// <summary>Marker of the real variant-named type.</summary>
    public required string RealVariantMarker { get; set; }
}

/// <summary>A real type whose name is the base branch id of a shipment.</summary>
public class ShipmentDefault
{
    /// <summary>Marker of the real base-branch-named type.</summary>
    public required string RealDefaultMarker { get; set; }
}

/// <summary>A real type whose name is the HTTP-context id of a shipment.</summary>
public class ShipmentHttp
{
    /// <summary>Marker of the real HTTP-named type.</summary>
    public required string RealHttpMarker { get; set; }
}

/// <summary>A label used only by server-sent events.</summary>
public class HttpOnlyLabel
{
    /// <summary>Label text.</summary>
    public required string LabelText { get; set; }
}

/// <summary>A customer profile used in both contexts; no other type takes its context ids.</summary>
public class CustomerProfile
{
    /// <summary>Display name of the customer.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Loyalty points balance.</summary>
    public required int LoyaltyPoints { get; set; }
}

/// <summary>A rebate; a concrete polymorphic base used in both contexts without name collisions.</summary>
[System.Text.Json.Serialization.JsonDerivedType(typeof(PercentRebate), "percent")]
public class Rebate
{
    /// <summary>Rebate code.</summary>
    public required string RebateCode { get; set; }
}

/// <summary>A percentage rebate.</summary>
public sealed class PercentRebate : Rebate
{
    /// <summary>Discount in percent.</summary>
    public required int DiscountPercent { get; set; }
}
