namespace Pena_e_Arte.Domain.Interfaces;

public record ConsentFormPdfData(
    string   StudioName,
    string   ClientFullName,
    string   ArtistFullName,
    DateTime AppointmentDate,
    string   SignatureText,
    DateTime SignedAt);

public interface IConsentFormPdfService
{
    byte[] Generate(ConsentFormPdfData data);
}
