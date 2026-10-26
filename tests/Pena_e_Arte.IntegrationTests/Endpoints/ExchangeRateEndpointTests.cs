using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.Infrastructure.Services.ExchangeRates;
using Pena_e_Arte.IntegrationTests.Infrastructure;
using StackExchange.Redis;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// GET /api/v1/public/exchange-rates/eur-all against real MySQL, and the BoA fetch path storing into the
// real exchange_rates table (unique index included). Anonymous on purpose (architecture.md "AllowAnonymous
// Exceptions"): guests see prices on /book, /artist/:slug and the gift-card page.
[Collection("Database")]
public class ExchangeRateEndpointTests(DatabaseFixture fixture)
{
    private static readonly TimeZoneInfo Tirane = TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane");
    private static DateOnly TodayTirane => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tirane));

    [Fact]
    public async Task NoRateStored_Returns404()
    {
        await ClearRatesAsync();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/public/exchange-rates/eur-all");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RateStored_Returns200WithTheNewestRateAndCacheHeader()
    {
        await ClearRatesAsync();
        await StoreAsync(90.70m, TodayTirane.AddDays(-1));
        await StoreAsync(90.74m, TodayTirane);
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/public/exchange-rates/eur-all");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(600));
        ExchangeRateResponse? body = await response.Content.ReadFromJsonAsync<ExchangeRateResponse>();
        body.Should().Be(new ExchangeRateResponse(90.74m, TodayTirane.ToString("yyyy-MM-dd"), "BoA-Fix", IsStale: false));
    }

    [Fact]
    public async Task RateEightDaysOld_IsStale()
    {
        await ClearRatesAsync();
        await StoreAsync(90.74m, TodayTirane.AddDays(-8));
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        ExchangeRateResponse? body = await client.GetFromJsonAsync<ExchangeRateResponse>("/api/v1/public/exchange-rates/eur-all");

        body!.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task Fetcher_SamePublicationTwice_StoresOneRow()
    {
        await ClearRatesAsync();
        string date = TodayTirane.ToString("dd.MM.yyyy");
        string page = $"""
            <span>Last update:</span>&nbsp;<span><b>{date}</b>&nbsp;&nbsp;<em><b>00:00:01</b></em></span></div>
            <TABLE><TR><TD>Main Currency</TD><TD>Albanian Lek per Foreign Currency Unit</TD></TR>
            <TR><TD nowrap>Euro</TD><TD nowrap>EUR</TD><td align="right" nowrap>90.74</td></TR></TABLE>
            """;
        IHttpClientFactory httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(BankOfAlbaniaRateFetcher.HttpClientName).Returns(_ => new HttpClient(new StaticPage(page)));

        await using (AppDbContext db = fixture.CreateDbContext(Guid.Empty))
        {
            BankOfAlbaniaRateFetcher fetcher = new(httpFactory, db, TimeProvider.System, NullLogger<BankOfAlbaniaRateFetcher>.Instance);
            (await fetcher.FetchAndStoreAsync(default)).Should().Be(RateFetchOutcome.Stored);
            (await fetcher.FetchAndStoreAsync(default)).Should().Be(RateFetchOutcome.AlreadyStored);
        }

        await using AppDbContext check = fixture.CreateDbContext(Guid.Empty);
        ExchangeRate row = await check.ExchangeRates.SingleAsync();
        row.Rate.Should().Be(90.740000m);
        row.RateDate.Should().Be(TodayTirane);
    }

    private async Task ClearRatesAsync()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await db.ExchangeRates.ExecuteDeleteAsync();
    }

    private async Task StoreAsync(decimal rate, DateOnly date)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        db.ExchangeRates.Add(ExchangeRate.Create("EUR", "ALL", date, rate, "BoA-Fix", DateTime.UtcNow, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task<IHost> BuildHost()
    {
        IHostBuilder builder = new HostBuilder()
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = false;
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddScoped<ICurrentTenant, CurrentTenantService>();
                    // Other public endpoints inject these; only their presence matters here.
                    services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
                    services.AddSingleton(Substitute.For<IGeoIpService>());
                    services.AddSingleton(Substitute.For<IUserAgentParser>());

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))));
                    services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
                    services.AddSingleton(TimeProvider.System);
                    services.AddScoped<IExchangeRateProvider, ExchangeRateProvider>();

                    Assembly applicationAssembly = typeof(ValidationBehavior<,>).Assembly;
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
                    services.AddValidatorsFromAssembly(applicationAssembly);
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

                    services.AddAuthentication();
                    services.AddAuthorization();
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<ExceptionMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapPublicEndpoints());
                });
            });

        return await builder.StartAsync();
    }

    private sealed class StaticPage(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
    }
}
