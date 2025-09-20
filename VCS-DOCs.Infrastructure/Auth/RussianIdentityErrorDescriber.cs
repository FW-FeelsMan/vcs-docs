using Microsoft.AspNetCore.Identity;

namespace VCS_DOCs.Infrastructure.Auth
{
    public class RussianIdentityErrorDescriber : IdentityErrorDescriber
    {
        public override IdentityError DefaultError()
            => new()
            {
                Code = nameof(DefaultError),
                Description = "Произошла неизвестная ошибка."
            };

        public override IdentityError ConcurrencyFailure()
            => new()
            {
                Code = nameof(ConcurrencyFailure),
                Description = "Ошибка синхронизации. Запись была изменена другим процессом."
            };

        public override IdentityError PasswordMismatch()
            => new()
            {
                Code = nameof(PasswordMismatch),
                Description = "Неверный пароль."
            };

        public override IdentityError InvalidToken()
            => new()
            {
                Code = nameof(InvalidToken),
                Description = "Недействительный токен подтверждения."
            };

        public override IdentityError LoginAlreadyAssociated()
            => new()
            {
                Code = nameof(LoginAlreadyAssociated),
                Description = "Учетная запись с таким логином уже существует."
            };

        public override IdentityError InvalidUserName(string userName)
            => new()
            {
                Code = nameof(InvalidUserName),
                Description = $"Недопустимое имя пользователя: '{userName}'."
            };

        public override IdentityError InvalidEmail(string email)
            => new()
            {
                Code = nameof(InvalidEmail),
                Description = $"Недопустимый адрес электронной почты: '{email}'."
            };

        public override IdentityError DuplicateUserName(string userName)
            => new()
            {
                Code = nameof(DuplicateUserName),
                Description = $"Имя пользователя '{userName}' уже занято."
            };

        public override IdentityError DuplicateEmail(string email)
            => new()
            {
                Code = nameof(DuplicateEmail),
                Description = $"Адрес электронной почты '{email}' уже используется."
            };

        public override IdentityError InvalidRoleName(string role)
            => new()
            {
                Code = nameof(InvalidRoleName),
                Description = $"Недопустимое имя роли: '{role}'."
            };

        public override IdentityError DuplicateRoleName(string role)
            => new()
            {
                Code = nameof(DuplicateRoleName),
                Description = $"Роль '{role}' уже существует."
            };

        public override IdentityError UserAlreadyHasPassword()
            => new()
            {
                Code = nameof(UserAlreadyHasPassword),
                Description = "Пароль уже задан для этой учетной записи."
            };

        public override IdentityError UserLockoutNotEnabled()
            => new()
            {
                Code = nameof(UserLockoutNotEnabled),
                Description = "Блокировка пользователя не включена."
            };

        public override IdentityError UserAlreadyInRole(string role)
            => new()
            {
                Code = nameof(UserAlreadyInRole),
                Description = $"Пользователь уже состоит в роли '{role}'."
            };

        public override IdentityError UserNotInRole(string role)
            => new()
            {
                Code = nameof(UserNotInRole),
                Description = $"Пользователь не состоит в роли '{role}'."
            };

        public override IdentityError PasswordTooShort(int length)
            => new()
            {
                Code = nameof(PasswordTooShort),
                Description = $"Пароль слишком короткий. Минимальная длина — {length} символов."
            };

        public override IdentityError PasswordRequiresUniqueChars(int uniqueChars)
            => new()
            {
                Code = nameof(PasswordRequiresUniqueChars),
                Description = $"Пароль должен содержать не менее {uniqueChars} уникальных символов."
            };

        public override IdentityError PasswordRequiresNonAlphanumeric()
            => new()
            {
                Code = nameof(PasswordRequiresNonAlphanumeric),
                Description = "Пароль должен содержать хотя бы один специальный символ."
            };

        public override IdentityError PasswordRequiresDigit()
            => new()
            {
                Code = nameof(PasswordRequiresDigit),
                Description = "Пароль должен содержать хотя бы одну цифру (0–9)."
            };

        public override IdentityError PasswordRequiresLower()
            => new()
            {
                Code = nameof(PasswordRequiresLower),
                Description = "Пароль должен содержать хотя бы одну строчную букву (a–z)."
            };

        public override IdentityError PasswordRequiresUpper()
            => new()
            {
                Code = nameof(PasswordRequiresUpper),
                Description = "Пароль должен содержать хотя бы одну прописную букву (A–Z)."
            };

        public override IdentityError RecoveryCodeRedemptionFailed()
            => new()
            {
                Code = nameof(RecoveryCodeRedemptionFailed),
                Description = "Неверный код восстановления."
            };
    }
}