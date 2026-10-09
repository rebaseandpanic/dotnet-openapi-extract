using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Contexts;

namespace ModernApi.Controllers;

/// <summary>Bodies serialized with the MVC JSON options and events serialized with the HTTP ones.</summary>
[ApiController]
[Route("contexts")]
public class SerializationContextsController : ControllerBase
{
    /// <summary>Order summary returned by a controller.</summary>
    [HttpGet("mvc-order")]
    public ActionResult<OrderSummary> MvcOrder() => new OrderSummary { OrderNumber = "A-1", ItemCount = 1 };

    /// <summary>A real DTO named like the HTTP schema of the order summary.</summary>
    [HttpGet("mvc-order-http-dto")]
    public ActionResult<OrderSummaryHttp> MvcOrderHttpDto() => new OrderSummaryHttp { RealDtoMarker = "real" };

    /// <summary>Envelope returned by a controller.</summary>
    [HttpGet("mvc-envelope")]
    public ActionResult<Envelope<OrderSummary>> MvcEnvelope() =>
        new Envelope<OrderSummary> { Payload = new OrderSummary { OrderNumber = "A-1", ItemCount = 1 } };

    /// <summary>A real DTO named like the HTTP schema of the envelope.</summary>
    [HttpGet("mvc-envelope-http-dto")]
    public ActionResult<OrderSummaryEnvelopeHttp> MvcEnvelopeHttpDto() => new OrderSummaryEnvelopeHttp { RealEnvelopeMarker = "real" };

    /// <summary>Shipment returned by a controller.</summary>
    [HttpGet("mvc-shipment")]
    public ActionResult<Shipment> MvcShipment() => new ExpressShipment { TrackingCode = "T", DeliveryWindowHours = 4 };

    /// <summary>Real DTOs named like the roles of the shipment union.</summary>
    [HttpGet("mvc-shipment-role-dtos")]
    public ActionResult<ExpressShipmentAsShipment> MvcShipmentVariantDto() => new ExpressShipmentAsShipment { RealVariantMarker = "real" };

    /// <summary>Real DTO named like the base branch of the shipment union.</summary>
    [HttpGet("mvc-shipment-default-dto")]
    public ActionResult<ShipmentDefault> MvcShipmentDefaultDto() => new ShipmentDefault { RealDefaultMarker = "real" };

    /// <summary>Real DTO named like the HTTP schema of a shipment.</summary>
    [HttpGet("mvc-shipment-http-dto")]
    public ActionResult<ShipmentHttp> MvcShipmentHttpDto() => new ShipmentHttp { RealHttpMarker = "real" };

    /// <summary>Customer profile returned by a controller.</summary>
    [HttpGet("mvc-customer")]
    public ActionResult<CustomerProfile> MvcCustomer() => new CustomerProfile { DisplayName = "Ann", LoyaltyPoints = 5 };

    /// <summary>Rebate returned by a controller.</summary>
    [HttpGet("mvc-rebate")]
    public ActionResult<Rebate> MvcRebate() => new PercentRebate { RebateCode = "C", DiscountPercent = 10 };

    /// <summary>Customer profile returned as a typed result, serialized with the HTTP options.</summary>
    [HttpGet("result-customer")]
    public Ok<CustomerProfile> ResultCustomer() => TypedResults.Ok(new CustomerProfile { DisplayName = "Ann", LoyaltyPoints = 5 });

    /// <summary>Customer profiles as server-sent events.</summary>
    [HttpGet("sse-customers")]
    public ServerSentEventsResult<CustomerProfile> SseCustomers() => TypedResults.ServerSentEvents(Customers());

    /// <summary>Rebates as server-sent events.</summary>
    [HttpGet("sse-rebates")]
    public ServerSentEventsResult<Rebate> SseRebates() => TypedResults.ServerSentEvents(Rebates());

    /// <summary>Order summaries as server-sent events.</summary>
    [HttpGet("sse-orders")]
    public ServerSentEventsResult<OrderSummary> SseOrders() => TypedResults.ServerSentEvents(Orders());

    /// <summary>Envelopes as server-sent events.</summary>
    [HttpGet("sse-envelopes")]
    public ServerSentEventsResult<Envelope<OrderSummary>> SseEnvelopes() => TypedResults.ServerSentEvents(Envelopes());

    /// <summary>Shipments as server-sent events.</summary>
    [HttpGet("sse-shipments")]
    public ServerSentEventsResult<Shipment> SseShipments() => TypedResults.ServerSentEvents(Shipments());

    /// <summary>Labels as server-sent events; the type is used by no controller body.</summary>
    [HttpGet("sse-labels")]
    public ServerSentEventsResult<HttpOnlyLabel> SseLabels() => TypedResults.ServerSentEvents(Labels());

    private static async IAsyncEnumerable<CustomerProfile> Customers()
    {
        await Task.Yield();
        yield return new CustomerProfile { DisplayName = "Ann", LoyaltyPoints = 5 };
    }

    private static async IAsyncEnumerable<Rebate> Rebates()
    {
        await Task.Yield();
        yield return new PercentRebate { RebateCode = "C", DiscountPercent = 10 };
    }

    private static async IAsyncEnumerable<OrderSummary> Orders()
    {
        await Task.Yield();
        yield return new OrderSummary { OrderNumber = "A-1", ItemCount = 1 };
    }

    private static async IAsyncEnumerable<Envelope<OrderSummary>> Envelopes()
    {
        await Task.Yield();
        yield return new Envelope<OrderSummary> { Payload = new OrderSummary { OrderNumber = "A-1", ItemCount = 1 } };
    }

    private static async IAsyncEnumerable<Shipment> Shipments()
    {
        await Task.Yield();
        yield return new ExpressShipment { TrackingCode = "T", DeliveryWindowHours = 4 };
    }

    private static async IAsyncEnumerable<HttpOnlyLabel> Labels()
    {
        await Task.Yield();
        yield return new HttpOnlyLabel { LabelText = "x" };
    }
}
