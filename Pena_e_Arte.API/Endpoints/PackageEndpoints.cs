using MediatR;
using Pena_e_Arte.Application.Packages.Commands;
using Pena_e_Arte.Application.Packages.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class PackageEndpoints
{
    public static void MapPackageEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/packages")
            .RequireAuthorization();

        // ClientAndAbove: clients need to see purchasable packages while booking, same posture
        // as DepositRuleEndpoints' own GET.
        group.MapGet("/", GetPackages).RequireAuthorization("ClientAndAbove");
        group.MapPost("/", CreatePackage).RequireAuthorization("OwnerOnly");
        group.MapPut("{id:guid}", UpdatePackage).RequireAuthorization("OwnerOnly");

        group.MapPost("/purchase", PurchasePackage).RequireAuthorization("ClientAndAbove");
        group.MapGet("/purchases/mine", GetMyPackagePurchases).RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> GetPackages(ISender mediator, CancellationToken ct)
    {
        List<PackageResponse> result = await mediator.Send(new GetPackagesQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreatePackage(
        CreatePackageRequest request, ISender mediator, CancellationToken ct)
    {
        PackageResponse result = await mediator.Send(new CreatePackageCommand(request), ct);
        return Results.Created($"/api/v1/packages/{result.Id}", result);
    }

    private static async Task<IResult> UpdatePackage(
        Guid id, UpdatePackageRequest request, ISender mediator, CancellationToken ct)
    {
        PackageResponse result = await mediator.Send(new UpdatePackageCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> PurchasePackage(
        PurchasePackageRequest request, ISender mediator, CancellationToken ct)
    {
        PurchasePackageResponse result = await mediator.Send(new PurchasePackageCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyPackagePurchases(ISender mediator, CancellationToken ct)
    {
        List<PackagePurchaseResponse> result = await mediator.Send(new GetMyPackagePurchasesQuery(), ct);
        return Results.Ok(result);
    }
}
