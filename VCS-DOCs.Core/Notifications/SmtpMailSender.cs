// VCS-DOCs.Support/Infrastructure/Mail/SmtpMailSender.cs
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace VCS_DOCs.Core.Notifications
{
    public sealed class SmtpMailSender : IMailSender
    {
        private readonly SmtpOptions _opt;
        public SmtpMailSender(IOptions<SmtpOptions> opt) => _opt = opt.Value;

        public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            using var msg = new MailMessage();

            // From
            var from = string.IsNullOrWhiteSpace(_opt.From)
                ? "no-reply@vcs-support.local"
                : _opt.From;
            msg.From = new MailAddress(from);

            // To / Subject / Body
            msg.To.Add(new MailAddress(toEmail));
            msg.Subject = subject;
            msg.Body = htmlBody;
            msg.IsBodyHtml = true;

            using var client = new SmtpClient(_opt.Host, _opt.Port)
            {
                EnableSsl = _opt.UseSsl,
                Credentials = _opt.HasCreds
                    ? new NetworkCredential(_opt.User, _opt.Password)
                    : CredentialCache.DefaultNetworkCredentials
            };

            await client.SendMailAsync(msg);
        }
    }
}
