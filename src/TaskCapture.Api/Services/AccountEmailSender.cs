using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using TaskCapture.Api.Options;

namespace TaskCapture.Api.Services;

public interface IAccountEmailSender
{
    Task SendLoginCodeAsync(
        string email,
        string code,
        int lifetimeMinutes,
        CancellationToken cancellationToken);
}

public sealed class MockAccountEmailSender : IAccountEmailSender
{
    public Task SendLoginCodeAsync(
        string email,
        string code,
        int lifetimeMinutes,
        CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SmtpAccountEmailSender(
    IOptions<AccessOptions> options) : IAccountEmailSender
{
    public async Task SendLoginCodeAsync(
        string email,
        string code,
        int lifetimeMinutes,
        CancellationToken cancellationToken)
    {
        var settings = options.Value.EmailCode.Delivery;
        if (string.IsNullOrWhiteSpace(settings.Host)
            || string.IsNullOrWhiteSpace(settings.FromAddress))
        {
            throw new InvalidOperationException(
                "メール送信にはAccess:EmailCode:Delivery:HostとFromAddressの設定が必要です。");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = "Task Capture ログイン確認コード",
            Body = $"Task Captureの確認コードは {code} です。\n\n{lifetimeMinutes}分以内に入力してください。心当たりがない場合は、このメールを無視してください。",
            IsBodyHtml = false
        };
        message.To.Add(email);

        using var smtp = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl
        };
        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            smtp.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }

        await smtp.SendMailAsync(message, cancellationToken);
    }
}
