using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VigiShield.Application.Services;
using VigiShield.Application.DTOs.Stream;
using VigiShield.Domain.Entities;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;
using Xunit;

namespace VigiShield.Tests;
public class CameraTests
{
    [Fact]
    public async Task Mobile_camera_is_configured_without_fake_ip_and_notifications_default_on()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var home = new Household(); db.Households.Add(home); await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["MediaMtx:ConfigPath"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".yml") }).Build();
        var service = new CameraService(db, config, new MediaMtxService(new FakeFactory(), config, NullLogger<MediaMtxService>.Instance));
        var dto = await service.CreateCameraAsync(home.Id, new UpdateCameraConfigRequest("Phone", "MobileWebRtc", null));
        Assert.True(dto.IsConfigured); Assert.Null(dto.CameraIp); Assert.NotEmpty(dto.StreamKey!);
        Assert.True((bool)dto.GetType().GetProperty("NotificationsEnabled")!.GetValue(dto)!);
    }
    [Fact]
    public async Task Switching_demo_to_mobile_rotates_shared_key_and_clears_ip_credentials()
    {
        await using var fixture = new TestDb();
        var cam = new CameraConfig { Household = new Household(), CameraIp = "DEMO", StreamKey = "demo", CameraPassword = "test-only", IsConfigured = true };
        fixture.Db.Add(cam); await fixture.Db.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var service = new CameraService(fixture.Db, config, new MediaMtxService(new FakeFactory(), config, NullLogger<MediaMtxService>.Instance));
        var result = await service.UpdateCameraAsync(cam.HouseholdId, cam.Id, new("Phone", "MobileWebRtc", null));
        Assert.NotEqual("demo", result.StreamKey);
        Assert.Null(result.CameraIp); Assert.False(result.HasPassword);
    }
    [Fact]
    public async Task Notification_setting_is_persisted_and_tenant_scoped()
    {
        await using var fixture = new TestDb(); var db = fixture.Db;
        var home = new Household(); var cam = new CameraConfig { Household = home };
        db.Add(cam); await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var service = new CameraService(db, config, new MediaMtxService(new FakeFactory(), config, NullLogger<MediaMtxService>.Instance));
        var method = typeof(CameraService).GetMethod("UpdateNotificationsAsync");
        Assert.NotNull(method);
        var dto = await (Task<CameraConfigDto>)method.Invoke(service, [home.Id, cam.Id, false])!;
        Assert.False(dto.NotificationsEnabled);
        db.ChangeTracker.Clear(); Assert.False((await db.CameraConfigs.SingleAsync()).NotificationsEnabled);
        var task = (Task<CameraConfigDto>)method.Invoke(service, [Guid.NewGuid(), cam.Id, true])!;
        var error = await Assert.ThrowsAsync<VigiShield.Common.Exceptions.AppException>(() => task);
        Assert.Equal(404, error.StatusCode);
    }
    [Theory]
    [InlineData(null, 554)]
    [InlineData("", 554)]
    [InlineData("no es una ip", 554)]
    [InlineData("192.168.1.82", 0)]
    [InlineData("192.168.1.82", 70000)]
    public async Task Ip_camera_requires_a_valid_address_and_port(string? ip, int port)
    {
        await using var fixture = new TestDb(); var db = fixture.Db;
        var home = new Household(); db.Add(home); await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var service = new CameraService(db, config, new MediaMtxService(new FakeFactory(), config, NullLogger<MediaMtxService>.Instance));
        var error = await Assert.ThrowsAsync<VigiShield.Common.Exceptions.AppException>(() =>
            service.CreateCameraAsync(home.Id, new UpdateCameraConfigRequest("Patio", "DirectRtsp", ip, port)));
        Assert.Equal(400, error.StatusCode);
        Assert.Empty(db.CameraConfigs);
    }
    [Fact]
    public async Task Zones_are_stored_in_camel_case_for_the_app_and_the_ai()
    {
        await using var fixture = new TestDb(); var db = fixture.Db;
        var home = new Household(); var cam = new CameraConfig { Household = home };
        db.Add(cam); await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var service = new CameraService(db, config, new MediaMtxService(new FakeFactory(), config, NullLogger<MediaMtxService>.Instance));
        var zona = new ZoneDto("z1", "door", "Puerta", [[0.1, 0.1], [0.5, 0.1], [0.5, 0.6]]);
        var dto = await service.UpdateZonesAsync(home.Id, cam.Id, new UpdateZonesRequest([zona]));
        Assert.Contains("\"type\":\"door\"", dto.ZonesJson);
        Assert.Contains("\"polygon\":[[0.1,0.1]", dto.ZonesJson);
        Assert.DoesNotContain("\"Type\"", dto.ZonesJson);
    }
}
public class FakeFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new Handler());
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
