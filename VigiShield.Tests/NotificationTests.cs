using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VigiShield.Application.Services;
using VigiShield.Application.DTOs.Events;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;
using Xunit;

namespace VigiShield.Tests;
public sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public AppDbContext Db { get; }
    public TestDb()
    {
        connection.Open();
        Db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        Db.Database.EnsureCreated();
    }
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
}
public class NotificationTests
{
    [Fact]
    public async Task Muted_camera_persists_event_and_snapshot_without_notification_services()
    {
        await using var fixture = new TestDb(); var db = fixture.Db;
        var home = new Household(); var cam = new CameraConfig { Household = home, NotificationsEnabled = false };
        db.Add(cam); await db.SaveChangesAsync();
        // Null transports deliberately fail if muted events reach either notification channel.
        var events = new EventService(db, null!, null!);
        var dto = await events.IngestEventAsync(new(home.Id, EventType.UnknownFace, cam.Id, "Phone", null, null, null, null, RiskLevel.High));
        Assert.NotNull(dto);
        Assert.False((bool)dto.GetType().GetProperty("NotificationsEnabled")!.GetValue(dto)!);
        Assert.Equal(1, await db.Events.CountAsync());
        cam.NotificationsEnabled = true; await db.SaveChangesAsync();
        var historical = await events.GetEventByIdAsync(home.Id, dto.Id);
        Assert.False((bool)historical.GetType().GetProperty("NotificationsEnabled")!.GetValue(historical)!);
    }
    [Fact]
    public async Task Ingest_rejects_camera_from_another_household()
    {
        await using var fixture = new TestDb(); var db = fixture.Db;
        var home = new Household(); var cam = new CameraConfig { Household = new Household() };
        db.AddRange(home, cam); await db.SaveChangesAsync();
        var events = new EventService(db, null!, null!);
        var error = await Assert.ThrowsAsync<AppException>(() => events.IngestEventAsync(new(home.Id, EventType.UnknownFace, cam.Id, null, null, null, null, null)));
        Assert.Equal(404, error.StatusCode); Assert.Empty(db.Events);
    }
}
