namespace Pena_e_Arte.Domain.Entities;

public class DepositRule : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal? AmountFixed { get; set; }
    public decimal? AmountPercent { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Hours of notice a client must give to self-cancel/reschedule without forfeiting
    /// the deposit. Null means "use AppointmentSelfServiceDefaults.CancellationWindowHours".
    /// </summary>
    public int? CancellationWindowHours { get; set; }

    /// <summary>
    /// Percent of the deposit refunded when a client cancels inside the notice window.
    /// 0 (default) forfeits the deposit entirely, matching pre-existing staff-cancel behavior.
    /// </summary>
    public int RefundPercentOnLateCancel { get; set; }
}
