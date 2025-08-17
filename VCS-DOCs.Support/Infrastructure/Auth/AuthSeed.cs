using Microsoft.AspNetCore.Identity;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Infrastructure.Auth
{
    public static class AuthSeed
    {
        public static async Task RunAsync(IServiceProvider sp)
        {
            var roleMgr = sp.GetRequiredService<RoleManager<IdentityRole>>();
            var userMgr = sp.GetRequiredService<UserManager<User>>();

            // 1) создаём роли, если их нет
            foreach (var r in new[] { Roles.BaseUser, Roles.SupportAgent, Roles.SupportAdmin })
                if (!await roleMgr.RoleExistsAsync(r))
                    await roleMgr.CreateAsync(new IdentityRole(r));

            // 2) всем пользователям без ролей — назначаем BaseUser (бережно)
            var users = userMgr.Users.ToList();
            foreach (var u in users)
            {
                var roles = await userMgr.GetRolesAsync(u);
                if (roles.Count == 0)
                    await userMgr.AddToRoleAsync(u, Roles.BaseUser);
            }

            // 3) (опционально) гарантируем роль саппорта конкретному ID
            var supportId = "6bbbcc2b-bcc8-4c20-b7ea-7993664339d2";
            var user = await userMgr.FindByIdAsync(supportId);
            if (user != null && !await userMgr.IsInRoleAsync(user, Roles.SupportAgent))
                await userMgr.AddToRoleAsync(user, Roles.SupportAgent);
        }
    }
}
