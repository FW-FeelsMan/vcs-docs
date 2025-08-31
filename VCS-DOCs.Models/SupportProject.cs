using System;

namespace VCS_DOCs.Models.Entities
{
    /// <summary>
    /// Набор возможностей интегрируемого проекта.
    /// Флаги, чтобы включать/выключать сценарии без перепрошивки.
    /// </summary>
    [Flags]
    public enum ProjectCapability : long
    {
        None = 0,
        PresenceRead = 1 << 0, // читать онлайн-статусы
        Kick = 1 << 1, // кикать пользователей
        UserDirectory = 1 << 2, // читать справочник пользователей
        TasksControl = 1 << 3, // управлять задачами (будущее)
        // резерв под расширение
    }

    /// <summary>
    /// Подключаемый внешний (или внутренний) проект, с которым работает Support.
    /// </summary>
    public class SupportProject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Код приложения (например, "VSupport", "VDocs"). Уникален.</summary>
        public string AppCode { get; set; } = string.Empty;

        /// <summary>Человекочитаемое имя (для UI).</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Базовый URL интеграции/АПИ (опционально).</summary>
        public string? BaseUrl
        {
            get; set;
        }

        /// <summary>API-ключ/токен (если используется HTTP-интеграция).</summary>
        public string? ApiKey
        {
            get; set;
        }

        /// <summary>Включён ли проект.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>Флаги доступных возможностей.</summary>
        public ProjectCapability Capabilities
        {
            get; set;
        }
            = ProjectCapability.PresenceRead | ProjectCapability.Kick;

        /// <summary>Создан (UTC).</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Последнее изменение (UTC).</summary>
        public DateTime? UpdatedUtc
        {
            get; set;
        }

        /// <summary>Произвольные доп. настройки JSON (если нужно).</summary>
        public string? MetadataJson
        {
            get; set;
        }
    }
}
