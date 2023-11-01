using System;

namespace Unlimotion.ViewModel.Models
{
    public class TaskUpdateEvent
    {
        public DateTime EventDateTime { get; }
        /// <summary>
        /// В случае с таском, полученным из файла Id - полный путь к файлу<br/>
        /// В случае с таском, полученным из Базы Данных, Id - это идентификатор записи в БД
        /// </summary>
        public string Id { get; }
        public UpdateType Type { get; }
        public TaskUpdateEvent(string id, UpdateType type)
        {
            Id = id;
            Type = type;
            EventDateTime = DateTime.Now;
        }

        public TaskUpdateEvent(string id, UpdateType type, DateTime eventDateTime)
        {
            Id = id;
            Type = type;
            EventDateTime = eventDateTime;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}