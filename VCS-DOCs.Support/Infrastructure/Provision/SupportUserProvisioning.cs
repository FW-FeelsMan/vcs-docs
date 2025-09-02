using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Infrastructure.Provision
{
    public interface ISupportUserProvisioning
    {
        Task<(User user, bool created)> EnsureUserExistsAsync(
            string login,
            string? email = null,
            string? fullName = null,
            string? organization = null,
            string? department = null,
            CancellationToken ct = default);
    }

    public sealed class SupportUserProvisioning : ISupportUserProvisioning
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public SupportUserProvisioning(
            ApplicationDbContext db,
            UserManager<User> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<(User user, bool created)> EnsureUserExistsAsync(
            string login,
            string? email = null,
            string? fullName = null,
            string? organization = null,
            string? department = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(login))
                throw new ArgumentException("login is required", nameof(login));

            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.UserName == login, ct);
            if (user != null)
            {
                var changed = false;

                if (!string.IsNullOrWhiteSpace(email) && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                {
                    user.Email = email;
                    user.NormalizedEmail = email?.ToUpperInvariant();
                    user.EmailConfirmed = true;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(fullName) && !string.Equals(user.FullName, fullName, StringComparison.Ordinal))
                {
                    user.FullName = fullName;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(organization) && !string.Equals(user.Organization, organization, StringComparison.Ordinal))
                {
                    user.Organization = organization;
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(department) && !string.Equals(user.Department, department, StringComparison.Ordinal))
                {
                    user.Department = department;
                    changed = true;
                }

                if (user.Access == 0)
                {
                    user.Access = 1;
                    changed = true;
                }

                if (changed)
                {
                    user.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                }

                return (user, false);
            }

            var now = DateTime.UtcNow;
            var newUser = new User
            {
                UserName = login,
                NormalizedUserName = login.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email?.ToUpperInvariant(),
                EmailConfirmed = !string.IsNullOrWhiteSpace(email),
                FullName = string.IsNullOrWhiteSpace(fullName) ? "Не установлено" : fullName,
                Organization = string.IsNullOrWhiteSpace(organization) ? "Не установлено" : organization,
                Department = string.IsNullOrWhiteSpace(department) ? "Не установлено" : department,
                Access = 1,
                CreatedAt = now,
                UpdatedAt = now,
                IsDeleted = false
            };

            // Случайный безопасный временный пароль (на шаге 2 будем высылать/сбрасывать по почте)
            var tempPassword = $"Aa1!{Guid.NewGuid():N}";

            var createRes = await _userManager.CreateAsync(newUser, tempPassword);
            if (!createRes.Succeeded)
                throw new InvalidOperationException("Failed to create user: " + string.Join("; ", createRes.Errors));

            // Гарантируем базовую роль
            if (!await _roleManager.RoleExistsAsync(Roles.BaseUser))
                await _roleManager.CreateAsync(new IdentityRole(Roles.BaseUser));

            if (!await _userManager.IsInRoleAsync(newUser, Roles.BaseUser))
                await _userManager.AddToRoleAsync(newUser, Roles.BaseUser);

            // Чтобы Accounts-таблица подхватила нового юзера через /delta — CreatedAt уже выставлен (UTC)
            return (newUser, true);
        }
    }
}
