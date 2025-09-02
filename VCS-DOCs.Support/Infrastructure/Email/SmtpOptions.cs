namespace VCS_DOCs.Support.Infrastructure.Email
{
    public class SmtpOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 2525;      // под smtp4dev
        public bool UseSsl { get; set; } = false;
        public string From { get; set; } = "no-reply@vcs-support.local";

        public string? User
        {
            get; set;
        }
        public string? Password
        {
            get; set;
        }
        public bool HasCreds => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);
    }
}
