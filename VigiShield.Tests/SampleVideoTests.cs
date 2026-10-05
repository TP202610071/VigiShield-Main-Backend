using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VigiShield.Application.DTOs.Stream;
using VigiShield.Application.Services;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Infrastructure.Services;
using Xunit;

namespace VigiShield.Tests;

public class SampleVideoTests
{
    private static CameraService Servicio(TestDb fixture)
    {
        var config = new ConfigurationBuilder().Build();
        return new CameraService(fixture.Db, config, new MediaMtxService(new FakeFactory(), config, NullLogger<MediaMtxService>.Instance));
    }

    [Fact]
    public async Task Sample_video_is_hidden_and_silent_and_only_analyzed_while_it_lasts()
    {
        await using var fixture = new TestDb();
        var home = new Household(); fixture.Db.Add(home); await fixture.Db.SaveChangesAsync();
        var service = Servicio(fixture);

        var sesion = await service.StartSampleVideoAsync(home.Id, null);

        Assert.NotNull(SampleVideoCatalog.Find(sesion.VideoKey));
        Assert.Equal(sesion.VideoKey, sesion.Camera.StreamKey);
        Assert.True(sesion.Camera.IsSample);
        Assert.False(sesion.Camera.NotificationsEnabled);   // sin WhatsApp ni alerta de emergencia
        Assert.False(sesion.Camera.IsDefault);
        Assert.Null(sesion.Camera.CameraIp);                 // MediaMTX nunca intenta tirar de ella
        Assert.Equal(SampleVideoCatalog.Find(sesion.VideoKey)!.ZonesJson, sesion.Camera.ZonesJson);
        Assert.Equal(1, sesion.Seen);
        Assert.Single(await service.GetCamerasAsync(home.Id));
        Assert.Single(await service.GetAllAiConfigAsync());

        // Al vencer, desaparece para la app y para la IA sin tarea de limpieza.
        var cam = await fixture.Db.CameraConfigs.SingleAsync();
        cam.SampleUntil = DateTime.UtcNow.AddSeconds(-1);
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(await service.GetCamerasAsync(home.Id));
        Assert.Empty(await service.GetAllAiConfigAsync());
        Assert.Null(await service.GetSampleVideoAsync(home.Id));
    }

    [Fact]
    public async Task Starting_again_returns_the_same_session_unless_another_is_asked()
    {
        await using var fixture = new TestDb();
        var home = new Household(); fixture.Db.Add(home); await fixture.Db.SaveChangesAsync();
        var service = Servicio(fixture);

        var primero = await service.StartSampleVideoAsync(home.Id, null);
        var igual = await service.StartSampleVideoAsync(home.Id, null);
        var otro = await service.StartSampleVideoAsync(home.Id, null, otro: true);

        Assert.Equal(primero.VideoKey, igual.VideoKey);
        Assert.NotEqual(primero.VideoKey, otro.VideoKey);
        Assert.Equal(2, otro.Seen);
        Assert.Single(await fixture.Db.CameraConfigs.ToListAsync());   // una sola cámara interna
        Assert.Equal(2, await fixture.Db.SampleVideoSessions.CountAsync());

        await service.StopSampleVideoAsync(home.Id);
        Assert.Null(await service.GetSampleVideoAsync(home.Id));
    }

    [Fact]
    public void Videos_are_spread_unseen_first_then_least_used()
    {
        var todos = SampleVideoCatalog.All.Select(v => v.Key).ToList();
        var usos = todos.ToDictionary(k => k, _ => 5);
        usos["ejemplo07"] = 1;
        usos["ejemplo03"] = 0;   // el menos usado, pero este hogar ya lo vio

        var elegido = CameraService.ElegirVideo(["ejemplo03"], usos, "ejemplo03", new Random(1));
        Assert.Equal("ejemplo07", elegido.Key);

        // Ya los vio todos: cualquiera menos el que acaba de ver.
        for (var i = 0; i < 50; i++)
            Assert.NotEqual("ejemplo02", CameraService.ElegirVideo(todos, usos, "ejemplo02", new Random(i)).Key);
    }

    [Fact]
    public async Task Sample_camera_cannot_be_edited_nor_become_the_first_camera()
    {
        await using var fixture = new TestDb();
        var home = new Household(); fixture.Db.Add(home); await fixture.Db.SaveChangesAsync();
        var service = Servicio(fixture);
        var sesion = await service.StartSampleVideoAsync(home.Id, null);

        await Assert.ThrowsAsync<AppException>(() =>
            service.UpdateZonesAsync(home.Id, sesion.Camera.Id, new UpdateZonesRequest([])));
        await Assert.ThrowsAsync<AppException>(() =>
            service.UpdateActiveAsync(home.Id, sesion.Camera.Id, false));

        // La primera cámara real del hogar sigue siendo la principal.
        var real = await service.CreateCameraAsync(home.Id, new UpdateCameraConfigRequest("Puerta", "MobileWebRtc", null));
        Assert.True(real.IsDefault);
        Assert.Equal(real.Id, (await service.GetDefaultCameraAsync(home.Id))!.Id);
    }

    [Fact]
    public void Every_sample_video_has_an_entry_zone_and_a_valid_path_name()
    {
        Assert.Equal(10, SampleVideoCatalog.All.Count);
        foreach (var v in SampleVideoCatalog.All)
        {
            Assert.Matches("^ejemplo[0-9]{2}$", v.Key);
            using var json = System.Text.Json.JsonDocument.Parse(v.ZonesJson);
            var zona = json.RootElement.GetProperty("zones")[0];
            Assert.True(zona.GetProperty("polygon").GetArrayLength() >= 3);
            Assert.False(string.IsNullOrWhiteSpace(v.Title));
            Assert.False(string.IsNullOrWhiteSpace(v.TitleEn));
        }
    }
}
