using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class BoothRentSchedule : TenantEntity
{
    public Guid ArtistId { get; set; }
    public decimal AmountFixed { get; set; }
    public RentFrequency Frequency { get; set; }
    public DateTime NextChargeDate { get; set; }
    public bool IsActive { get; set; } = true;

    public Artist Artist { get; set; } = null!;
    public ICollection<BoothRentCharge> Charges { get; set; } = [];
}
