namespace VCS_DOCs.Core.Interfaces;
public interface IUserInfoProvider
{
    Task<long> GetUserStorageLimitAsync(string shortUserId);
}
