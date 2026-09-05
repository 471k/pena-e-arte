namespace Pena_e_Arte.Contracts.Requests;

public record UpdateArtistPortfolioRequest(List<PortfolioImageInput> Images);

public record PortfolioImageInput(string ImageUrl, string? Style, string? Category);
