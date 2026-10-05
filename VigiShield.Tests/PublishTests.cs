using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;
using Xunit;

namespace VigiShield.Tests;
public class PublishTests
{
    private const string Offer = "v=0\r\ns=offer\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=sendonly\r\n";
    [Fact]
    public async Task Publish_is_scoped_proxies_sdp_and_deletes_using_opaque_handle()
    {
        await using var f = new Fixture();
        var result = await f.Service.PublishAsync(f.Home, f.Camera, Offer);
        Assert.Equal("v=0\r\ns=answer\r\n", result.Sdp);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.Equal("https://gateway.invalid/live/abcdef123456/whip", f.Http.Requests[0].Url);
        Assert.Equal(Offer, f.Http.Requests[0].Body);
        Assert.True(f.Http.Requests[0].HasKey);
        var duplicate = await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(f.Home, f.Camera, Offer));
        Assert.Equal(409, duplicate.StatusCode);
        var forbidden = await Assert.ThrowsAsync<AppException>(() => f.Service.DeleteAsync(Guid.NewGuid(), f.Camera, result.SessionId));
        Assert.Equal(404, forbidden.StatusCode); Assert.Single(f.Http.Requests);
        await f.Service.DeleteAsync(f.Home, f.Camera, result.SessionId);
        Assert.Equal("DELETE", f.Http.Requests[1].Method);
        Assert.Equal("https://gateway.invalid/live/abcdef123456/whip/session", f.Http.Requests[1].Url);
        Assert.True(f.Http.Requests[1].HasKey);
        await f.Service.PublishAsync(f.Home, f.Camera, Offer);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("http://gateway.invalid/live/")]
    [InlineData("https://user:password@gateway.invalid/live/")]
    [InlineData("https://gateway.invalid/live/?query=1")]
    public async Task Missing_or_unsafe_configuration_disables_publish(string? url)
    {
        await using var f = new Fixture(new() { ["MediaMtx:WhipBaseUrl"] = url });
        var error = await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(f.Home, f.Camera, Offer));
        Assert.Equal(503, error.StatusCode); Assert.Empty(f.Http.Requests);
    }
    [Fact]
    public async Task Missing_key_disables_publish()
    {
        await using var f = new Fixture(new() { ["MediaMtx:WhipGatewayKey"] = null });
        var error = await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(f.Home, f.Camera, Offer));
        Assert.Equal(503, error.StatusCode); Assert.Empty(f.Http.Requests);
    }
    [Theory]
    [InlineData("https://evil.invalid/live/abcdef123456/whip/session")]
    [InlineData("http://gateway.invalid/live/abcdef123456/whip/session")]
    [InlineData("https://gateway.invalid:444/live/abcdef123456/whip/session")]
    [InlineData("/outside/session")]
    [InlineData("/live/other/whip/session")]
    [InlineData("/live/abcdef123456/whip/%2e%2e/secret")]
    [InlineData("/live/abcdef123456/whip/session?next=elsewhere")]
    public async Task Untrusted_location_never_becomes_a_delete_target(string location)
    {
        await using var f = new Fixture();
        f.Http.Respond = _ => { var r = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("v=0\r\ns=answer\r\n", Encoding.UTF8, "application/sdp") }; r.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute); return r; };
        var error = await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(f.Home, f.Camera, Offer));
        Assert.Equal(502, error.StatusCode); Assert.Single(f.Http.Requests);
    }
    [Theory]
    [InlineData("")]
    [InlineData("not-sdp")]
    public async Task Invalid_offer_is_rejected_before_network(string sdp)
    {
        await using var f = new Fixture();
        var error = await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(f.Home, f.Camera, sdp));
        Assert.Equal(400, error.StatusCode); Assert.Empty(f.Http.Requests);
    }
    [Fact]
    public async Task Oversized_offer_is_rejected_before_network()
    {
        await using var f = new Fixture();
        var error = await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(f.Home, f.Camera, Offer + new string('x', 65536)));
        Assert.Equal(400, error.StatusCode); Assert.Empty(f.Http.Requests);
    }
    [Fact]
    public async Task Wrong_household_and_nonmobile_sources_cannot_publish()
    {
        await using var f = new Fixture();
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(Guid.NewGuid(), f.Camera, Offer))).StatusCode);
        await f.EditCamera(c => c.StreamMode = StreamMode.DirectRtsp);
        Assert.Equal(400, (await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(f.Home, f.Camera, Offer))).StatusCode);
        Assert.Empty(f.Http.Requests);
    }
    [Theory]
    [InlineData("v=0\r\ns=x\r\n")]
    [InlineData("v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\n")]
    [InlineData("v=0\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\n")]
    public async Task Only_video_offers_are_accepted(string offer)
    {
        await using var f = new Fixture();
        Assert.Equal(400, (await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(f.Home, f.Camera, offer))).StatusCode);
        Assert.Empty(f.Http.Requests);
    }
    [Fact]
    public async Task Invalid_answer_is_cleaned_up_and_does_not_reserve_camera()
    {
        await using var f = new Fixture();
        f.Http.Respond = request => {
            var r = new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent);
            r.Headers.Location = new Uri("/live/abcdef123456/whip/session", UriKind.Relative);
            r.Content = new StringContent(new string('x', 65537), Encoding.UTF8, "application/sdp"); return r;
        };
        Assert.Equal(502, (await Assert.ThrowsAsync<AppException>(() => f.Service.PublishAsync(f.Home, f.Camera, Offer))).StatusCode);
        Assert.Equal("DELETE", f.Http.Requests.Last().Method);
        f.Http.Respond = null;
        await f.Service.PublishAsync(f.Home, f.Camera, Offer);
    }
    [Fact]
    public async Task Stopping_service_closes_live_sessions()
    {
        await using var f = new Fixture();
        await f.Service.PublishAsync(f.Home, f.Camera, Offer);
        await f.Service.StopAsync(CancellationToken.None);
        Assert.Equal("DELETE", f.Http.Requests.Last().Method);
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly ServiceProvider provider;
        public Guid Home { get; } = Guid.NewGuid();
        public Guid Camera { get; } = Guid.NewGuid();
        public RecordingFactory Http { get; } = new();
        public WhipPublishService Service { get; }
        public Fixture(Dictionary<string,string?>? overrides = null)
        {
            connection.Open();
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
            provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                db.Add(new CameraConfig { Id = Camera, Household = new Household { Id = Home }, StreamMode = StreamMode.MobileWebRtc, StreamKey = "abcdef123456", IsConfigured = true });
                db.SaveChanges();
            }
            var settings = new Dictionary<string,string?> { ["MediaMtx:WhipBaseUrl"] = "https://gateway.invalid/live/", ["MediaMtx:WhipGatewayKey"] = Guid.NewGuid().ToString("N") };
            if (overrides != null) foreach (var pair in overrides) settings[pair.Key] = pair.Value;
            Service = new(provider.GetRequiredService<IServiceScopeFactory>(), Http, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), NullLogger<WhipPublishService>.Instance);
        }
        public async Task EditCamera(Action<CameraConfig> edit)
        {
            using var scope = provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            edit(await db.CameraConfigs.SingleAsync()); await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() { Service.Dispose(); await provider.DisposeAsync(); await connection.DisposeAsync(); }
    }
    internal sealed class RecordingFactory : IHttpClientFactory
    {
        public List<(string Method, string Url, string? Body, bool HasKey)> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }
        public HttpClient CreateClient(string name) => new(new Handler(this));
        private sealed class Handler(RecordingFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                owner.Requests.Add((request.Method.Method, request.RequestUri!.ToString(), request.Content == null ? null : await request.Content.ReadAsStringAsync(ct), request.Headers.Contains("X-VigiShield-Publish-Key")));
                if (owner.Respond != null) return owner.Respond(request);
                var response = new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent);
                response.Content = new StringContent("v=0\r\ns=answer\r\n", Encoding.UTF8, "application/sdp");
                response.Headers.Location = new Uri("/live/abcdef123456/whip/session", UriKind.Relative);
                return response;
            }
        }
    }
}
