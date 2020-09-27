using Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace netcoreconf2020.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;
        private readonly Random _random;
        private readonly SampleDbContext _dbContext;
        private readonly HttpClient _httpClient;

        public WeatherForecastController(ILogger<WeatherForecastController> logger, Random random, SampleDbContext dbContext, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _random = random;
            _dbContext = dbContext;
            _httpClient = httpClientFactory.CreateClient(nameof(WeatherForecastController));
        }

        [HttpGet]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateTime.Now.AddDays(index),
                TemperatureC = _random.Next(-20, 55),
                Summary = Summaries[_random.Next(Summaries.Length)]
            })
            .ToArray();
        }

        [HttpGet("private")]
        [Authorize]
        public string GetLogInUserName() 
        {
            return User.Identity.Name;
        }

        [HttpGet("url")]
        public ActionResult<string> GetUrl(int id)
        {
            var url = _dbContext.Urls.Find(id);
            if (url is null)
                return NotFound();
            return Ok(url.Address);
        }

        [HttpGet("proxy")]
        public async Task<string> GetExternalForecast()
        {
            var response = await _httpClient.GetAsync("/weather?q=Gandia%2Ces&units=metric");
            return await response.Content.ReadAsStringAsync();
        }
    }
}
