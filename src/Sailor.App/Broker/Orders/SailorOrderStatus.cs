namespace Sailor.App.Broker.Orders;

public enum SailorOrderStatus
{
    Created = 0,
    PendingSubmit = 1,
    Submitted = 2,
    PartiallyFilled = 3,
    Filled = 4,
    CancelRequested = 5,
    Cancelled = 6,
    Rejected = 7,
    Failed = 8,
    DryRun = 9
}
