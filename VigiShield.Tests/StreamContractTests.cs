using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using VigiShield.Controllers;
using VigiShield.Application.DTOs.Stream;
using Xunit;

namespace VigiShield.Tests;
public class StreamContractTests
{
    [Theory]
    [InlineData("Publish", "cameras/{cameraId:guid}/publish")]
    [InlineData("DeletePublish", "cameras/{cameraId:guid}/publish/{sessionId:guid}")]
    [InlineData("UpdateNotifications", "cameras/{cameraId:guid}/notifications")]
    public async Task Endpoints_are_authenticated_and_secondary_residents_are_forbidden(string name, string route)
    {
        var method = typeof(StreamController).GetMethod(name);
        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(route, method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()!.Template);
        var constructor = typeof(StreamController).GetConstructors().Single();
        var args = constructor.GetParameters().Select(p => p.ParameterType == typeof(IConfiguration) ? new ConfigurationBuilder().Build() : (object?)null).ToArray();
        var controller = (StreamController)constructor.Invoke(args);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Secondary"), new Claim("householdId", Guid.NewGuid().ToString())], "test")) } };
        var inputs = method.GetParameters().Select(p => p.ParameterType == typeof(Guid) ? (object)Guid.NewGuid() : p.ParameterType == typeof(PublishRequest) ? new PublishRequest("v=0\r\n") : p.ParameterType == typeof(UpdateNotificationsRequest) ? new UpdateNotificationsRequest(false) : (object)CancellationToken.None).ToArray();
        var task = (Task)method.Invoke(controller, inputs)!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task);
        if (result is ActionResult<CameraConfigDto> camera) result = camera.Result;
        if (result is ActionResult<PublishResponse> publish) result = publish.Result;
        Assert.IsType<ForbidResult>(result);
    }
}
