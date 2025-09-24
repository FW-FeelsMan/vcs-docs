using System.Threading.Tasks;

namespace VCS_DOCs.Core.Notifications
{
    public interface IMailSender
    {
        Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
    }
}
