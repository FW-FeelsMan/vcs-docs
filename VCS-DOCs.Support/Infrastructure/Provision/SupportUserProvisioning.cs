using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Infrastructure.Provision
{
    public interface ISupportUserProvisioning
    {
        /// <summary>
        /// Гарантирует наличие пользователя. Если не было — создаёт c временным паролем.
        /// </summary>
        /// <returns>(user, created, tempPassword)</returns>
        Task<(User user, bool created, string? tempPassword)> EnsureUserExistsAsync(
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

        public async Task<(User user, bool created, string? tempPassword)> EnsureUserExistsAsync(
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

                return (user, false, null);
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

            // ВАЖНО: генерим пароль без спецсимволов, длина <= 20 (Web-ограничение)
            var tempPassword = GenerateTempPassword(16, requireNonAlnum: false);

            var createRes = await _userManager.CreateAsync(newUser, tempPassword);
            if (!createRes.Succeeded)
                throw new InvalidOperationException("Failed to create user: " + string.Join("; ", createRes.Errors.Select(e => $"{e.Code}: {e.Description}")));

            if (!await _roleManager.RoleExistsAsync(Roles.BaseUser))
                await _roleManager.CreateAsync(new IdentityRole(Roles.BaseUser));

            if (!await _userManager.IsInRoleAsync(newUser, Roles.BaseUser))
                await _userManager.AddToRoleAsync(newUser, Roles.BaseUser);

            return (newUser, true, tempPassword);
        }

        private static string GenerateTempPassword(int length = 16, bool requireNonAlnum = false)
        {
            // Жёстко укладываемся в [6..20]
            if (length < 6) length = 6;
            if (length > 20) length = 20;

            const string U = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // без I/O
            const string L = "abcdefghijkmnopqrstuvwxyz"; // без l
            const string D = "23456789";                  // без 0/1
            const string S = "!@#$%^*";                   // если когда-то понадобится

            var pool = requireNonAlnum ? (U + L + D + S) : (U + L + D);
            using var rng = RandomNumberGenerator.Create();

            char Pick(string src)
            {
                var b = new byte[4];
                rng.GetBytes(b);
                var idx = (int)(BitConverter.ToUInt32(b, 0) % (uint)src.Length);
                return src[idx];
            }

            var chars = new List<char>(length) { Pick(U), Pick(L), Pick(D) }; // гарантируем классы
            while (chars.Count < length) chars.Add(Pick(pool));

            // перемешаем Фишер–Йейтсом
            for (int i = chars.Count - 1; i > 0; i--)
            {
                var b = new byte[4];
                rng.GetBytes(b);
                int j = (int)(BitConverter.ToUInt32(b, 0) % (uint)(i + 1));
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars.ToArray());
        }
    }
}