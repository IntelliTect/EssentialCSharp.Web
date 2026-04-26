using System.Security.Claims;
using System.Text.Json;
using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Web.Controllers;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace EssentialCSharp.Web.Tests;

public class ChatControllerTests
{
    [Test]
    public async Task StreamMessage_MissingCaptchaToken_Returns403WithCaptchaRequired()
    {
        var controller = CreateController();

        await controller.StreamMessage(new ChatMessageRequest { Message = "hello" });

        var body = await ReadJsonResponse(controller.HttpContext.Response);
        await Assert.That(controller.HttpContext.Response.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(body["errorCode"].GetString()).IsEqualTo("captcha_required");
    }

    [Test]
    public async Task StreamMessage_InvalidCaptcha_Returns403WithCaptchaFailed()
    {
        var captchaService = new Mock<ICaptchaService>();
        captchaService
            .Setup(service => service.VerifyAsync("bad-token", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HCaptchaResult { Success = false });

        var controller = CreateController(captchaService: captchaService.Object);

        await controller.StreamMessage(new ChatMessageRequest { Message = "hello", CaptchaToken = "bad-token" });

        var body = await ReadJsonResponse(controller.HttpContext.Response);
        await Assert.That(controller.HttpContext.Response.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(body["errorCode"].GetString()).IsEqualTo("captcha_failed");
    }

    [Test]
    public async Task StreamMessage_CaptchaServiceUnavailable_Returns503WithCaptchaUnavailable()
    {
        var captchaService = new Mock<ICaptchaService>();
        captchaService
            .Setup(service => service.VerifyAsync("token", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HCaptchaResult?)null);

        var controller = CreateController(captchaService: captchaService.Object);

        await controller.StreamMessage(new ChatMessageRequest { Message = "hello", CaptchaToken = "token" });

        var body = await ReadJsonResponse(controller.HttpContext.Response);
        await Assert.That(controller.HttpContext.Response.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(body["errorCode"].GetString()).IsEqualTo("captcha_unavailable");
    }

    private static ChatController CreateController(
        IAIChatService? aiChatService = null,
        ICaptchaService? captchaService = null)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "TestAuth"))
        };
        httpContext.Response.Body = new MemoryStream();

        var controller = new ChatController(
            Mock.Of<ILogger<ChatController>>(),
            aiChatService ?? new Mock<IAIChatService>(MockBehavior.Strict).Object,
            captchaService ?? new Mock<ICaptchaService>(MockBehavior.Strict).Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return controller;
    }

    private static async Task<Dictionary<string, JsonElement>> ReadJsonResponse(HttpResponse response)
    {
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }
}
