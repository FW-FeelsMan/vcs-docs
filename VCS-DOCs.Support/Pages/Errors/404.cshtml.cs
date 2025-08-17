using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VCS_DOCs.Support.Pages.Errors
{
    public class _404Model : PageModel
    {
        private readonly IConfiguration _cfg;
        public _404Model(IConfiguration cfg) => _cfg = cfg;

        [FromRoute]
        public int? Code
        {
            get; set;
        }
        public string PageTitle { get; private set; } = "Ошибка";
        public string Title { get; private set; } = "Произошла ошибка";
        public string Description { get; private set; } = "Попробуйте обновить страницу или вернитесь на главную.";

        public string? OriginalPath
        {
            get; private set;
        }
        public string? TraceId
        {
            get; private set;
        }

        public void OnGet(int? code)
        {
            var status = code ?? HttpContext.Response.StatusCode;
            if (status == 0) status = 404;
            Code = status;

            var feat = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
            if (feat != null)
            {
                var q = string.IsNullOrEmpty(feat.OriginalQueryString) ? "" : feat.OriginalQueryString;
                OriginalPath = $"{feat.OriginalPathBase}{feat.OriginalPath}{q}";
            }

            TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            (Title, Description) = GetMessage(status);
            PageTitle = $"{status} — {Title}";
            Response.StatusCode = status;
        }

        private static (string title, string desc) GetMessage(int code) => code switch
        {
            401 => ("Требуется вход", "Доступ к ресурсу разрешён только зарегистрированным пользователям. Войдите, чтобы продолжить."),
            403 => ("Доступ запрещён", "У вас нет прав для просмотра этого ресурса или доступ был отозван."),
            404 => ("Страница не найдена", "Возможно, она была удалена, недоступна или вы ошиблись адресом."),
            410 => ("Ссылка больше не активна", "Срок действия ссылки истёк или она была удалена."),
            429 => ("Слишком много запросов", "Слишком частые обращения. Подождите немного и попробуйте снова."),
            500 => ("Внутренняя ошибка сервера", "Что-то пошло не так на нашей стороне. Мы уже разбираемся."),
            _ => ("Ошибка", "Произошла ошибка. Проверьте адрес или вернитесь на главную.")
        };
    }
}
