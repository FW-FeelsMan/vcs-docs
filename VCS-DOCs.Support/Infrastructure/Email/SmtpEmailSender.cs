using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VCS_DOCs.Support.Infrastructure.Email
{
    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _opt;
        private readonly ILogger<SmtpEmailSender> _log;

        public SmtpEmailSender(IOptions<SmtpOptions> opt, ILogger<SmtpEmailSender> log)
        {
            _opt = opt.Value;
            _log = log;
        }

        public async Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
        {
            using var msg = new MailMessage
            {
                From = new MailAddress(_opt.From),
                Subject = subject,
                Body = html,
                IsBodyHtml = true
            };
            msg.To.Add(new MailAddress(to));

            using var client = new SmtpClient(_opt.Host, _opt.Port)
            {
                EnableSsl = _opt.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = _opt.HasCreds
                    ? new NetworkCredential(_opt.User, _opt.Password)
                    : CredentialCache.DefaultNetworkCredentials
            };

            _log.LogInformation("Sending email to {To} via {Host}:{Port}", to, _opt.Host, _opt.Port);
            await client.SendMailAsync(msg);
        }
    }
}
