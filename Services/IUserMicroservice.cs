using System.Threading;
using System.Threading.Tasks;

namespace VCS_DOCs.Services
{
	public interface IUserMicroservice
	{
		string UserId { get; }

		bool ShouldKeepRunningAfterUserDisconnect { get; }

		Task StartAsync(CancellationToken cancellationToken);

		Task StopAsync(CancellationToken cancellationToken);

		Task DelayAndStopAsync()
		{
			if (ShouldKeepRunningAfterUserDisconnect)
			{
				return Task.CompletedTask;
			}

			return Task.Run(async () =>
			{
				await Task.Delay(30000); // 30 секунд
				await StopAsync(CancellationToken.None);
			});
		}
	}
}