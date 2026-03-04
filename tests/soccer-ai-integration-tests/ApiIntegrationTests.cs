using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using FluentAssertions;
using SoccerAi.Application.Models;
using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Infrastructure.Persistence;

namespace SoccerAi.IntegrationTests;

[Collection("IntegrationTests")]
public class ApiIntegrationTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(CustomWebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        
        // Seed the in-memory database
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var request = new { Username = "admin", Password = "wrongpassword" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var request = new { Username = "shivm", Password = "Shivm_Adm1n_!Soccer#Gpt" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", request);
        
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new Exception($"Login Failed with {response.StatusCode}: {content}");
        }
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResponse = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        loginResponse.Should().NotBeNull();
        loginResponse!["token"]?.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetCombinations_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/combinations?date=2025-01-01");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAnalysis_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        var token = await GetAuthToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/analyze?date=2025-01-01&language=en");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<string> GetAuthToken()
    {
        var request = new { Username = "shivm", Password = "Shivm_Adm1n_!Soccer#Gpt" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", request);
        var loginResponse = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        return loginResponse!["token"]!.ToString();
    }

    [Fact]
    public async Task GetCombinations_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        var token = await GetAuthToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync("/api/combinations?date=2025-01-01&language=en");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
