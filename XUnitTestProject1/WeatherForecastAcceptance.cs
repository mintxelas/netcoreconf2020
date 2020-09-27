using Acheve.AspNetCore.TestHost.Security;
using Acheve.TestHost;
using ApprovalTests;
using ApprovalTests.Reporters;
using ApprovalTests.Scrubber;
using Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using netcoreconf2020.Controllers;
using NSubstitute;
using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace XUnitTestProject1
{
    [UseReporter(typeof(DiffReporter))]
    public class WeatherForecastAcceptance: IDisposable
    {
        private readonly TestServer _server;
        private readonly Random _random;
        private readonly WireMockServer _api;

        public WeatherForecastAcceptance()
        {
            _api = WireMockServer.Start();
            _random = Substitute.For<Random>();
            _server = new TestServer(new WebHostBuilder()
                .ConfigureTestServices(services => 
                {
                    services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.WriteIndented = true);
                    services.AddScoped(_ => _random);
                    services.AddAuthentication(TestServerDefaults.AuthenticationScheme).AddTestServer(options => options.NameClaimType = "name");
                    services.RemoveAll<DbContextOptions<SampleDbContext>>();
                    services.AddDbContext<SampleDbContext>(builder => builder.UseInMemoryDatabase("test"));

                    var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
                    config["OpenWeatherApi:Url"] = _api.Urls[0];
                    config["OpenWeatherApi:Host"] = "some-host";
                    config["OpenWeatherApi:Key"] = "some-key";
                })
                .ConfigureAppConfiguration(builder => 
                {
                    builder.AddJsonFile("appsettings.test.json").AddUserSecrets<WeatherForecastAcceptance>();
                })
                .UseEnvironment("Test")
                .UseStartup<netcoreconf2020.Startup>()
            );
        }

        [Fact]
        public async Task ReturnAForecast()
        {
            GivenAForecast();
            var response = await WhenForecastIsRequested();
            await ThenTheExpectedContentIsReturned(response);
        }

        private void GivenAForecast()
        {
            _random.Next(Arg.Any<int>(), Arg.Any<int>()).Returns(1, 2, 3, 4, 5);
            _random.Next(Arg.Any<int>()).Returns(1, 5, 9, 2, 4);
        }

        private Task<HttpResponseMessage> WhenForecastIsRequested()
        {
            return _server.CreateHttpApiRequest<WeatherForecastController>(c => c.Get()).GetAsync();
        }

        private async Task ThenTheExpectedContentIsReturned(HttpResponseMessage message)
        {
            message.EnsureSuccessStatusCode();
            _random.Received(5).Next(-20, 55);
            _random.Received(5).Next(10);
            var scrubDate = ScrubberUtils.RemoveLinesContaining("\"date\":");
            Approvals.Verify(await message.Content.ReadAsStringAsync(), scrubDate);
        }

        [Fact]
        public async Task RejectConnectionIfNotAuthenticated()
        {
            var response = await WhenARequestIsMadeToTheProtectedEndpoint();
            ThenUnauthorizedIsReturned(response);
        }

        private Task<HttpResponseMessage> WhenARequestIsMadeToTheProtectedEndpoint()
        {
            return _server.CreateHttpApiRequest<WeatherForecastController>(c => c.GetLogInUserName()).GetAsync();
        }

        private void ThenUnauthorizedIsReturned(HttpResponseMessage message)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, message.StatusCode);
        }

        [Fact]
        public async Task ReturnLogInUserName()
        {
            var name = GivenALogedInUser();
            var response = await WhenAuthenticatedRequestIsMade(name);
            await ThenTheExpectedStringIsReturned(response, name);
        }

        private string GivenALogedInUser()
        {
            return "some-user-name";
        }

        private Task<HttpResponseMessage> WhenAuthenticatedRequestIsMade(string name)
        {
            return _server.CreateHttpApiRequest<WeatherForecastController>(c => c.GetLogInUserName())
                .WithIdentity(new[] { new Claim("name", name) })
                .GetAsync();
        }

        private async Task ThenTheExpectedStringIsReturned(HttpResponseMessage message, string text)
        {
            message.EnsureSuccessStatusCode();
            Assert.Equal(text, await message.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task GetUrl()
        {
            const string url = "some-url";
            const int id = 123;
            GivenAUrlInDatabase(id, url);
            var response = await WhenIdIsRequested(id);
            await ThenTheExpectedStringIsReturned(response, url);
        }

        private void GivenAUrlInDatabase(int id, string url)
        {
            var dbContext = _server.Services.GetRequiredService<SampleDbContext>();
            dbContext.Urls.Add(new Url
            {
                Id = id,
                Address = url
            });
            dbContext.SaveChanges();
        }

        private Task<HttpResponseMessage> WhenIdIsRequested(int id)
        {
            return _server.CreateHttpApiRequest<WeatherForecastController>(c => c.GetUrl(id)).GetAsync();
        }

        [Fact]
        public async Task GetNotFound()
        {
            GivenAUrlInDatabase(123, "some-url");
            var response = await WhenIdIsRequested(321);
            ThenNotFoundIsReturned(response);
        }

        private void ThenNotFoundIsReturned(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task ActAsProxy()
        {
            GivenAThirdPartyForecast();
            var response = await WhenProxiedForecastIsRequested();
            await ThenTheForecastIsRetrieved(response);
        }

        private void GivenAThirdPartyForecast()
        {
            _api.Given(Request.Create()
                    .WithPath("/weather")
                    .WithParam("q", "Gandia,es")
                    .WithParam("units", "metric")
                    .WithHeader("x-rapidapi-host", "some-host")
                    .WithHeader("x-rapidapi-key", "some-key")
                    .UsingGet())
                .RespondWith(Response.Create().WithBody("some-body"));
        }

        private Task<HttpResponseMessage> WhenProxiedForecastIsRequested()
        {
            return _server.CreateHttpApiRequest<WeatherForecastController>(c => c.GetExternalForecast()).GetAsync();
        }

        private async Task ThenTheForecastIsRetrieved(HttpResponseMessage response)
        {
            Approvals.Verify(await response.Content.ReadAsStringAsync());
        }

        public void Dispose()
        {
            var dbContext = _server.Services.GetRequiredService<SampleDbContext>();
            dbContext.Urls.RemoveRange(dbContext.Urls);
            dbContext.SaveChanges();

            _api.Dispose();

            _server.Dispose();
        }
    }
}
