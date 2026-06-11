using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Queries;

public record GetStudioQrCodeQuery(Guid StudioId, string Format) : IRequest<QrCodeResponse>;

public class GetStudioQrCodeValidator : AbstractValidator<GetStudioQrCodeQuery>
{
    private static readonly HashSet<string> AllowedFormats = ["png", "svg"];

    public GetStudioQrCodeValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.Format)
            .Must(f => AllowedFormats.Contains(f?.ToLowerInvariant() ?? string.Empty))
            .WithMessage("Format must be 'png' or 'svg'.");
    }
}

public class GetStudioQrCodeHandler(IAppDbContext db, IQrCodeService qrCode)
    : IRequestHandler<GetStudioQrCodeQuery, QrCodeResponse>
{
    private const string BaseUrl = "https://penaearte.com/s/";

    public async Task<QrCodeResponse> Handle(GetStudioQrCodeQuery query, CancellationToken ct)
    {
        // Approved: public QR code endpoint — no auth, points to portfolio URL only
        string slug = await db.Studios
            .IgnoreQueryFilters()
            .Where(s => s.Id == query.StudioId && s.IsActive)
            .Select(s => s.Slug)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), query.StudioId);

        string url = BaseUrl + slug;

        return query.Format.ToLowerInvariant() switch
        {
            "svg" => new QrCodeResponse(System.Text.Encoding.UTF8.GetBytes(qrCode.GenerateSvg(url)), "image/svg+xml", slug),
            _     => new QrCodeResponse(qrCode.GeneratePng(url), "image/png", slug),
        };
    }
}
