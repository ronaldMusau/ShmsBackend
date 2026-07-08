namespace ShmsBackend.Data.Models.Enums;

public enum TenantStatus
{
    Inactive = 0,      // Created, no payment
    Pending = 1,       // Payment done, email not verified
    Active = 2,        // Email verified + password set
    PaymentFailed = 3  // Payment failed
}
